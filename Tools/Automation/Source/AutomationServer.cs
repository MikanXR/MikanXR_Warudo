using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace WarudoAutomation
{
	// One command as a handler sees it: the tokens after the namespace name plus the raw line
	public class CommandRequest
	{
		public string Namespace;
		public List<string> Args;
		public string Line;

		public string Verb => Args.Count > 0 ? Args[0] : string.Empty;

		// The raw rest of the line after the namespace and the first argCount arguments
		public string RawRest(int argCount)
		{
			return AutomationProtocol.RemainderAfterTokens(Line, argCount + 1);
		}
	}

	// A reply the handler answers later, after it returned (end-of-frame captures)
	public class DeferredReply
	{
		private readonly AutomationServer _server;
		private readonly string _errorPrefix;
		private bool _sent;

		internal DeferredReply(AutomationServer server, string errorPrefix)
		{
			_server = server;
			_errorPrefix = errorPrefix;
		}

		internal bool IsSent => _sent;

		public void Send(List<string> lines)
		{
			if (_sent)
			{
				return;
			}
			_sent = true;
			_server.FinishDeferredReply(lines);
		}

		public void Fail(string message)
		{
			Send(new List<string> { _errorPrefix + (string.IsNullOrEmpty(message) ? "failed" : message) });
		}
	}

	// Returns true with outLines filled on success, or false with outError set
	public delegate bool CommandHandler(CommandRequest request, List<string> outLines, out string outError);

	// The loopback TCP command server. Lives on a DontDestroyOnLoad object and pumps its socket
	// from Update, so every command runs on Unity's main thread against live scene state. One
	// client at a time; a second connection waits in the backlog until the first closes.
	// The Playground plugin creates it and shuts it down across hot reloads.
	public class AutomationServer : MonoBehaviour
	{
		public const int DefaultPort = 21140;
		public const string PortArgumentPrefix = "-automationPort=";

		private class CommandProvider
		{
			public List<string> HelpLines;
			public CommandHandler Handler;
		}

		private readonly SortedDictionary<string, CommandProvider> _providers =
			new SortedDictionary<string, CommandProvider>(StringComparer.Ordinal);

		private TcpListener _listener;
		private Socket _client;
		private readonly StringBuilder _readBuffer = new StringBuilder();
		private readonly Queue<string> _pendingLines = new Queue<string>();
		private readonly byte[] _receiveBuffer = new byte[4096];
		private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
		private DeferredReply _deferredReply;
		private DeferredReply _openDeferral;
		private string _currentErrorPrefix;
		private bool _quitRequested;
		private bool _shutDown;
		private float _nextOpenAttempt;

		private const float k_openRetrySeconds = 1f;

		public static AutomationServer Instance { get; private set; }
		public int Port { get; private set; }
		public bool IsListening => _listener != null;
		public bool HasClient => _client != null;

		public static AutomationServer Create()
		{
			var go = new GameObject("WarudoAutomation");
			DontDestroyOnLoad(go);
			go.hideFlags = HideFlags.HideInHierarchy;
			return go.AddComponent<AutomationServer>();
		}

		public void RegisterNamespace(string name, IEnumerable<string> helpLines, CommandHandler handler)
		{
			_providers[name] = new CommandProvider { HelpLines = new List<string>(helpLines), Handler = handler };
		}

		public IEnumerable<string> HelpLines
		{
			get
			{
				foreach (CommandProvider provider in _providers.Values)
				{
					foreach (string helpLine in provider.HelpLines)
					{
						yield return helpLine;
					}
				}
			}
		}

		// Called from a handler that answers later: the reply it returns is ignored, and further
		// command lines wait until the deferred reply is sent
		public DeferredReply DeferReply()
		{
			_openDeferral = new DeferredReply(this, _currentErrorPrefix);
			return _openDeferral;
		}

		// Shuts Warudo down once the current reply has gone out
		public void RequestQuit()
		{
			_quitRequested = true;
		}

		// Closes the listener now rather than when Unity destroys the object at the end of the
		// frame, so a hot-reloaded server created in the same frame can bind the port
		public void Shutdown()
		{
			_shutDown = true;
			Close();
		}

		private void Awake()
		{
			Instance = this;
			Port = ParsePortArgument();
			Open();
		}

		private void OnDestroy()
		{
			Close();
			if (Instance == this)
			{
				Instance = null;
			}
		}

		private void OnApplicationQuit()
		{
			Close();
		}

		private static int ParsePortArgument()
		{
			foreach (string arg in Environment.GetCommandLineArgs())
			{
				if (arg.StartsWith(PortArgumentPrefix, StringComparison.OrdinalIgnoreCase)
					&& int.TryParse(arg.Substring(PortArgumentPrefix.Length), out int port)
					&& port > 0 && port < 65536)
				{
					return port;
				}
			}

			return DefaultPort;
		}

		private void Open()
		{
			try
			{
				_listener = new TcpListener(IPAddress.Loopback, Port);
				_listener.Start(1);
				Debug.Log($"[WarudoAutomation] listening on 127.0.0.1:{Port}");
			}
			catch (Exception e)
			{
				_listener = null;
				Debug.LogError($"[WarudoAutomation] failed to listen on port {Port}: {e.Message}");
			}
		}

		private void Close()
		{
			DropClient();
			if (_listener != null)
			{
				_listener.Stop();
				_listener = null;
			}
		}

		private void Update()
		{
			if (_listener == null)
			{
				// A previous server may still hold the port for a moment after a hot reload
				if (!_shutDown && Time.unscaledTime >= _nextOpenAttempt)
				{
					_nextOpenAttempt = Time.unscaledTime + k_openRetrySeconds;
					Open();
				}
				return;
			}

			if (_client == null && _listener.Pending())
			{
				AcceptClient();
			}

			if (_client != null)
			{
				ReadAvailable();
				DispatchPendingLines();
			}

			if (_quitRequested && _deferredReply == null)
			{
				_quitRequested = false;
				Debug.Log("[WarudoAutomation] quit requested");
				Close();
				Application.Quit();
			}
		}

		private void AcceptClient()
		{
			try
			{
				_client = _listener.AcceptSocket();
				_client.NoDelay = true;
				_client.SendTimeout = 5000;
				_readBuffer.Clear();
				_pendingLines.Clear();
				_decoder.Reset();
				Debug.Log($"[WarudoAutomation] client connected from {_client.RemoteEndPoint}");
			}
			catch (Exception e)
			{
				_client = null;
				Debug.LogWarning($"[WarudoAutomation] accept failed: {e.Message}");
			}
		}

		private void DropClient()
		{
			if (_client == null)
			{
				return;
			}

			try
			{
				// Half-close first so a reply still in flight (the quit acknowledgement) arrives
				_client.Shutdown(SocketShutdown.Send);
			}
			catch (Exception)
			{
			}

			_client.Close();
			_client = null;
			_deferredReply = null;
			Debug.Log("[WarudoAutomation] client disconnected");
		}

		private void ReadAvailable()
		{
			try
			{
				// A readable socket with nothing to read has been closed by the peer
				if (_client.Poll(0, SelectMode.SelectRead) && _client.Available == 0)
				{
					DropClient();
					return;
				}

				while (_client.Available > 0)
				{
					int count = _client.Receive(_receiveBuffer, 0, Math.Min(_receiveBuffer.Length, _client.Available), SocketFlags.None);
					if (count <= 0)
					{
						DropClient();
						return;
					}

					char[] chars = new char[_decoder.GetCharCount(_receiveBuffer, 0, count)];
					_decoder.GetChars(_receiveBuffer, 0, count, chars, 0);
					_readBuffer.Append(chars);
				}
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[WarudoAutomation] read failed: {e.Message}");
				DropClient();
				return;
			}

			string buffered = _readBuffer.ToString();
			int newlinePos;
			int start = 0;
			while ((newlinePos = buffered.IndexOf('\n', start)) >= 0)
			{
				string line = buffered.Substring(start, newlinePos - start);
				if (line.EndsWith("\r"))
				{
					line = line.Substring(0, line.Length - 1);
				}
				_pendingLines.Enqueue(line);
				start = newlinePos + 1;
			}

			if (start > 0)
			{
				_readBuffer.Remove(0, start);
			}
		}

		private void DispatchPendingLines()
		{
			while (_client != null && _deferredReply == null && _pendingLines.Count > 0)
			{
				HandleCommandLine(_pendingLines.Dequeue());
			}
		}

		private void HandleCommandLine(string line)
		{
			var tokens = new List<string>();
			if (!AutomationProtocol.TokenizeCommandLine(line, tokens, out string parseError))
			{
				SendReply(new List<string> { "parse: " + parseError });
				return;
			}

			// An empty line is a no-op that still answers, so a client stays in sync
			if (tokens.Count == 0)
			{
				SendReply(new List<string>());
				return;
			}

			string namespaceName = tokens[0];
			if (!_providers.TryGetValue(namespaceName, out CommandProvider provider))
			{
				SendReply(new List<string>
				{
					$"unknown command '{namespaceName}'; valid namespaces: {string.Join(", ", _providers.Keys)}"
				});
				return;
			}

			var request = new CommandRequest
			{
				Namespace = namespaceName,
				Args = tokens.GetRange(1, tokens.Count - 1),
				Line = line,
			};

			_currentErrorPrefix = namespaceName + (request.Args.Count > 0 ? " " + request.Args[0] : string.Empty) + ": ";
			_openDeferral = null;

			var outLines = new List<string>();
			string outError;
			bool ok;
			try
			{
				ok = provider.Handler(request, outLines, out outError);
			}
			catch (Exception e)
			{
				ok = false;
				outError = DescribeException(e);
			}

			if (_openDeferral != null)
			{
				// The handler answers later; a handler that failed after deferring answers now
				DeferredReply deferral = _openDeferral;
				_openDeferral = null;
				if (!deferral.IsSent)
				{
					_deferredReply = deferral;
					if (!ok)
					{
						deferral.Fail(outError);
					}
				}
				return;
			}

			if (ok)
			{
				SendReply(outLines);
			}
			else
			{
				SendReply(new List<string> { _currentErrorPrefix + (string.IsNullOrEmpty(outError) ? "failed" : outError) });
			}
		}

		internal void FinishDeferredReply(List<string> lines)
		{
			_deferredReply = null;
			SendReply(lines);
		}

		private void SendReply(List<string> contentLines)
		{
			if (_client == null)
			{
				return;
			}

			try
			{
				byte[] bytes = Encoding.UTF8.GetBytes(AutomationProtocol.FrameReply(contentLines));
				_client.Send(bytes);
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[WarudoAutomation] send failed: {e.Message}");
				DropClient();
			}
		}

		public static string DescribeException(Exception e)
		{
			while (e is System.Reflection.TargetInvocationException && e.InnerException != null)
			{
				e = e.InnerException;
			}

			return $"{e.GetType().Name}: {e.Message}";
		}
	}
}
