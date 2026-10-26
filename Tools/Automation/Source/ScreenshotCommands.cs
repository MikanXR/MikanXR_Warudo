using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace WarudoAutomation
{
	// screenshot: the window as presented (Warudo's UI included) or the texture one camera renders into
	public static class ScreenshotCommands
	{
		public const string DefaultWindowFile = "warudo_window.png";
		public const string DefaultCameraFile = "warudo_camera.png";

		public static void Register(AutomationServer server)
		{
			server.RegisterNamespace("screenshot", new[]
			{
				"screenshot window [path]",
				"screenshot camera <target> [path]",
				"screenshot cameras",
			}, Handle);
		}

		private static string ResolveOutputPath(CommandRequest request, int argIndex, string defaultFile)
		{
			string path = request.Args.Count > argIndex ? request.Args[argIndex] : defaultFile;
			return Path.GetFullPath(path);
		}

		private static bool Handle(CommandRequest request, List<string> outLines, out string outError)
		{
			outError = null;
			List<string> args = request.Args;
			switch (request.Verb)
			{
				case "window":
				{
					// The back buffer is only complete at the end of the frame, so the reply waits
					string path = ResolveOutputPath(request, 1, DefaultWindowFile);
					DeferredReply reply = AutomationServer.Instance.DeferReply();
					AutomationServer.Instance.StartCoroutine(CaptureWindow(path, reply));
					return true;
				}

				case "camera":
				{
					if (args.Count < 2)
					{
						outError = "usage: screenshot camera <target> [path]";
						return false;
					}
					if (!Targets.TryResolve(args[1], out object target, out outError))
					{
						return false;
					}
					Camera camera = target as Camera ?? Targets.AsGameObject(target)?.GetComponent<Camera>();
					if (camera == null)
					{
						outError = $"'{args[1]}' is not a camera";
						return false;
					}
					return StartCameraCapture(camera, ResolveOutputPath(request, 2, DefaultCameraFile), out outError);
				}

				case "cameras":
					foreach (Camera camera in Object.FindObjectsOfType<Camera>(true))
					{
						string target = camera.targetTexture != null
							? $"texture={camera.targetTexture.width}x{camera.targetTexture.height}"
							: "texture=screen";
						outLines.Add($"#{camera.GetInstanceID()} {Targets.PathOf(camera.gameObject)} {(camera.isActiveAndEnabled ? "enabled" : "disabled")} depth={camera.depth} {target}");
					}
					return true;
			}

			outError = "usage: screenshot window|camera|cameras";
			return false;
		}

		private static IEnumerator CaptureWindow(string path, DeferredReply reply)
		{
			yield return new WaitForEndOfFrame();

			Texture2D texture = null;
			try
			{
				// The back buffer as presented, read at the end of the frame
				texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
				texture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
				texture.Apply(false);
				WritePng(texture, path);
				reply.Send(new List<string> { path });
			}
			catch (Exception e)
			{
				reply.Fail(AutomationServer.DescribeException(e));
			}
			finally
			{
				if (texture != null)
				{
					Object.Destroy(texture);
				}
			}
		}

		// Reads the texture a camera renders into as it last rendered (what Spout publishes),
		// without rendering it again. A camera that draws to the screen has no texture of its own
		// to read, so the window capture covers it.
		private static bool StartCameraCapture(Camera camera, string path, out string outError)
		{
			outError = null;
			RenderTexture source = camera.targetTexture;
			if (source == null)
			{
				outError = $"{Targets.PathOf(camera.gameObject)} draws to the screen; use screenshot window";
				return false;
			}

			DeferredReply reply = AutomationServer.Instance.DeferReply();
			int width = source.width;
			int height = source.height;
			AsyncGPUReadback.Request(source, 0, TextureFormat.RGBA32, request =>
			{
				if (request.hasError)
				{
					reply.Fail("GPU readback failed");
					return;
				}

				var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
				try
				{
					texture.LoadRawTextureData(request.GetData<byte>());
					texture.Apply();
					WritePng(texture, path);
					reply.Send(new List<string> { path, $"{width}x{height} {source.name}" });
				}
				catch (Exception e)
				{
					reply.Fail(AutomationServer.DescribeException(e));
				}
				finally
				{
					Object.Destroy(texture);
				}
			});
			return true;
		}

		private static void WritePng(Texture2D texture, string path)
		{
			string folder = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(folder))
			{
				Directory.CreateDirectory(folder);
			}
			File.WriteAllBytes(path, texture.EncodeToPNG());
		}
	}
}
