using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace WarudoAutomation
{
	// help, app and log: the namespaces every drive starts with
	public static class CoreCommands
	{
		public static void Register(AutomationServer server)
		{
			server.RegisterNamespace("help", new[] { "help" }, (CommandRequest request, List<string> outLines, out string outError) =>
			{
				outError = null;
				outLines.AddRange(server.HelpLines);
				return true;
			});

			server.RegisterNamespace("app", new[]
			{
				"app info",
				"app assemblies [filter]",
				"app quit",
			}, HandleApp);

			server.RegisterNamespace("log", new[]
			{
				"log tail <lineCount> [minLevel]",
				"log since <sequence> [minLevel]",
				"log mark [text...]",
				"log clear",
				"log file <lineCount>",
			}, HandleLog);
		}

		private static bool HandleApp(CommandRequest request, List<string> outLines, out string outError)
		{
			outError = null;
			switch (request.Verb)
			{
				case "info":
				{
					outLines.Add($"product {Application.productName}");
					outLines.Add($"version {Application.version}");
					outLines.Add($"unity {Application.unityVersion}");
					outLines.Add($"plugin {WarudoAutomationPlugin.PluginVersion}");
					outLines.Add($"port {AutomationServer.Instance.Port}");
					outLines.Add($"frame {Time.frameCount}");
					outLines.Add($"time {Time.realtimeSinceStartup.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
					outLines.Add($"fps {(Time.smoothDeltaTime > 0f ? (1f / Time.smoothDeltaTime).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "0")}");
					outLines.Add($"screen {Screen.width}x{Screen.height}");
					outLines.Add($"scenes {string.Join(", ", Targets.LoadedScenes().Select(s => s.name))}");
					Warudo.Core.Scenes.Scene warudoScene = Warudo.Core.Context.OpenedScene;
					outLines.Add($"warudo_scene {(warudoScene != null ? warudoScene.Name : "none")}");
					outLines.Add($"assets {(warudoScene != null ? warudoScene.GetAssetList().Count : 0)}");
					outLines.Add($"pipeline {Warudo.Core.Context.RenderingPipelineManager.CurrentPipeline}");
					outLines.Add($"log {Application.consoleLogPath}");
					outLines.Add($"data {Application.streamingAssetsPath}");
					return true;
				}

				case "assemblies":
				{
					string filter = request.Args.Count > 1 ? request.Args[1] : null;
					foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
					{
						string name = assembly.GetName().Name;
						if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
						{
							continue;
						}
						string location;
						try
						{
							location = assembly.IsDynamic ? "dynamic" : assembly.Location;
						}
						catch (Exception)
						{
							location = string.Empty;
						}
						outLines.Add($"{name} {assembly.GetName().Version} {(string.IsNullOrEmpty(location) ? "-" : location)}");
					}
					return true;
				}

				case "quit":
					AutomationServer.Instance.RequestQuit();
					return true;
			}

			outError = "usage: app info|assemblies|quit";
			return false;
		}

		private static bool TryParseOptionalLevel(CommandRequest request, int argIndex, out LogLevel level, out string outError)
		{
			outError = null;
			level = LogLevel.Info;
			if (request.Args.Count > argIndex && !LogRing.TryParseLevel(request.Args[argIndex], out level))
			{
				outError = $"unknown log level '{request.Args[argIndex]}' (info|warning|error)";
				return false;
			}
			return true;
		}

		private static void AddEntries(List<LogEntry> entries, List<string> outLines)
		{
			foreach (LogEntry entry in entries)
			{
				string level = entry.Type == LogType.Exception ? "exception" : entry.Level.ToString().ToLowerInvariant();
				outLines.Add($"{entry.Sequence} {entry.Time:HH:mm:ss.fff} {level} {entry.Message}");

				// Exceptions carry their stack, since the message alone rarely locates them
				if (entry.Type == LogType.Exception && !string.IsNullOrEmpty(entry.StackTrace))
				{
					foreach (string frame in entry.StackTrace.Split('\n').Where(f => f.Trim().Length > 0))
					{
						outLines.Add("    " + frame.TrimEnd('\r'));
					}
				}
			}
		}

		private static bool HandleLog(CommandRequest request, List<string> outLines, out string outError)
		{
			outError = null;
			switch (request.Verb)
			{
				case "tail":
				{
					if (request.Args.Count < 2 || !int.TryParse(request.Args[1], out int count) || count < 0)
					{
						outError = "usage: log tail <lineCount> [minLevel]";
						return false;
					}
					if (!TryParseOptionalLevel(request, 2, out LogLevel level, out outError))
					{
						return false;
					}
					List<LogEntry> entries = LogRing.Snapshot(0, level);
					AddEntries(entries.Skip(Math.Max(0, entries.Count - count)).ToList(), outLines);
					return true;
				}

				case "since":
				{
					if (request.Args.Count < 2 || !long.TryParse(request.Args[1], out long sequence))
					{
						outError = "usage: log since <sequence> [minLevel]";
						return false;
					}
					if (!TryParseOptionalLevel(request, 2, out LogLevel level, out outError))
					{
						return false;
					}
					AddEntries(LogRing.Snapshot(sequence, level), outLines);
					return true;
				}

				case "mark":
				{
					string text = request.Args.Count > 1 ? request.RawRest(1) : "mark";
					Debug.Log($"[WarudoAutomation] {text}");
					outLines.Add(LogRing.LastSequence.ToString());
					return true;
				}

				case "clear":
					LogRing.Clear();
					return true;

				case "file":
				{
					if (request.Args.Count < 2 || !int.TryParse(request.Args[1], out int count) || count < 0)
					{
						outError = "usage: log file <lineCount>";
						return false;
					}
					outLines.AddRange(TailFile(Application.consoleLogPath, count));
					return true;
				}
			}

			outError = "usage: log tail|since|mark|clear|file";
			return false;
		}

		// Player.log is held open for writing by Unity, so it is read with a shared handle
		private static IEnumerable<string> TailFile(string path, int count)
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
			{
				return new[] { $"no log file at {path}" };
			}

			using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
			using (var reader = new StreamReader(stream, Encoding.UTF8))
			{
				var lines = new Queue<string>();
				string line;
				while ((line = reader.ReadLine()) != null)
				{
					lines.Enqueue(line);
					if (lines.Count > count)
					{
						lines.Dequeue();
					}
				}
				return lines.ToList();
			}
		}
	}
}
