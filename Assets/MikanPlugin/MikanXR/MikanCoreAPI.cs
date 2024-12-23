using System;
using UnityEngine;
using System.Reflection;

namespace MikanXR
{
	public class MikanCoreAPI : IDisposable
	{
		private bool _isInitialized = false;
		private string _clientUniqueID = "";

		private MikanLogger _logger;
		public MikanLogger Logger => _logger;

		private MikanMessageClient _messageClient;
		public MikanMessageClient MessageClient => _messageClient;

		private System.Type _spoutSenderType = null;
		private MethodInfo _spoutSenderUpdateMethodInfo = null;
		private MethodInfo _spoutSenderDisposeMethodInfo = null;
		private System.Object _colorSenderObject = null;
		private System.Object _depthSenderObject = null;

		public MikanCoreAPI()
		{
			_clientUniqueID = Guid.NewGuid().ToString();
			_logger = new MikanLogger();
			_messageClient = new MikanMessageClient(_logger);
		}

		public void Dispose()
		{
			Shutdown();

			_messageClient.Dispose();
			_logger.Dispose();
		}

		public MikanAPIResult Initialize(MikanLogLevel minLogLevel)
		{
			if (_isInitialized)
			{
				return MikanAPIResult.Success;
			}

			LoggerSettings loggerSettings = new LoggerSettings()
			{
				min_log_level = minLogLevel,
				log_filename = "MikanCore.log",
				enable_console = true,
			};
			_logger.Init(loggerSettings);

			MikanAPIResult result = _messageClient.Initialize();
			if (result != MikanAPIResult.Success)
			{
				return result;
			}

			// Use reflection to fetch the KlakSpout API
			FetchSpoutAPI();

			_isInitialized = true;

			return MikanAPIResult.Success;
		}

		public MikanAPIResult Shutdown()
		{
			_messageClient.Disconnect();
			_isInitialized = false;

			return MikanAPIResult.Success;
		}

		public int GetClientAPIVersion()
		{
			return Constants.MIKAN_CLIENT_API_VERSION;
		}

		public string GetClientUniqueID()
		{
			return _clientUniqueID;
		}

		public bool GetIsInitialized()
		{
			return _isInitialized;
		}

		public MikanAPIResult SetClientInfo(string clientInfo)
		{
			return _messageClient.SetClientInfo(clientInfo);
		}

		public MikanAPIResult GetRenderTargetDescriptor(out MikanRenderTargetDescriptor out_descriptor)
		{
			out_descriptor = new MikanRenderTargetDescriptor();

			return MikanAPIResult.GeneralError;
		}

		public MikanAPIResult AllocateRenderTargetTextures(MikanRenderTargetDescriptor descriptor)
		{
			// TODO
			return MikanAPIResult.GeneralError;
		}

		public MikanAPIResult FreeRenderTargetTextures()
		{
			// TODO
			return MikanAPIResult.GeneralError;
		}

		public MikanAPIResult WriteColorRenderTargetTexture(RenderTexture color_texture)
		{
			// TODO

			return MikanAPIResult.GeneralError;
		}

		public MikanAPIResult WriteDepthRenderTargetTexture(
			RenderTexture depth_texture,
			float z_near,
			float z_far)
		{
			// TODO
			return MikanAPIResult.GeneralError;
		}

		public MikanAPIResult Connect(string host = "", string port = "")
		{
			return _messageClient.Connect(host, port);
		}

		public bool GetIsConnected()
		{
			return _messageClient.IsConnected;
		}

		public MikanAPIResult Disconnect(ushort code, string reason)
		{
			if (GetIsConnected())
			{
				_messageClient.Disconnect(code, reason);
				return MikanAPIResult.Success;
			}

			return MikanAPIResult.NotConnected;
		}

		public MikanAPIResult FetchNextEvent(out string message)
		{
			return _messageClient.FetchNextEvent(out message);
		}

		public MikanAPIResult SendRequestJSON(
			string jsonString)
		{
			if (_messageClient.IsConnected)
			{
				_messageClient.SendRequest(jsonString);

				return MikanAPIResult.Success;
			}
			else
			{
				return MikanAPIResult.NotConnected;
			}
		}

		private void FetchSpoutAPI()
		{
			Assembly sysData = Assembly.Load("Klak.Spout.Runtime");
			_spoutSenderType = sysData.GetType("Klak.Spout.Sender");

			_spoutSenderUpdateMethodInfo = _spoutSenderType.GetMethod("Update");
			_spoutSenderDisposeMethodInfo = _spoutSenderType.GetMethod("Dispose");
		}

		private System.Object CreateSender(string senderName, Texture renderTexture)
		{
			return Activator.CreateInstance(
				   _spoutSenderType,
				   BindingFlags.Instance
				   | BindingFlags.Public
				   | BindingFlags.NonPublic,
				   null,
				   new System.Object[] { senderName, renderTexture }, // or your actual constructor arguments
				   null
				);
		}

		private void ReleaseSender(ref System.Object senderObject)
		{
			if (senderObject != null)
			{
				if (_spoutSenderDisposeMethodInfo != null)
				{
					_spoutSenderDisposeMethodInfo.Invoke(senderObject, null);
				}

				senderObject = null;
			}
		}

	}
}
