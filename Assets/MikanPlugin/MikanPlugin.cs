
using Warudo.Core.Attributes;
using Warudo.Core.Plugins;
using Warudo.Plugins.Core;
using Warudo.Plugins.MikanPlugin.Nodes;
using MikanXR;
using Warudo.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;
using System;

namespace Warudo.Plugins.MikanPlugin {
    [PluginType(
        Id = "Warudo.MikanXR",
        Name = "MIKAN_XR",
        Description = "MIKAN_XR_DESCRIPTION",
        Author = "MikanXR Org",
        Version = "1.0.0",
         NodeTypes = new[] {
             typeof(OnMikanConnectedNode)
         })]
    public class MikanPlugin : Plugin {

		private MikanLogger _logger = null;
		public MikanLogger Logger => _logger;

		public MikanAPI ClientAPI { get; } = new();
        public MikanEventController EventController { get; } = new();

        protected override void OnCreate() 
        {
            if (!InitializeMikanAPI())
			{
				DisposeMikanAPI();
			}
        }

		protected override void OnDestroy() {
            base.OnDestroy();
            ClientAPI.Shutdown();
        }

		private bool InitializeMikanAPI()
		{
			// Set the logging callback first that so that we can can see init errors
			_logger = ClientAPI.CoreAPI.Logger;
			_logger.AddLogCallback(Log);

			// Attempt to initialize the Mikan API (load MikanCore.dll)
			if (ClientAPI.Initialize(MikanLogLevel.Info) != MikanAPIResult.Success)
			{
				Warudo.Core.Utils.Log.Error("Failed to initialize MikanAPI");
				return false;
			}

			// Determine which graphics API is being used by Unity
			//MikanClientGraphicsApi unityGraphicsAPI = MikanClientGraphicsApi.UNKNOWN;
			//switch (SystemInfo.graphicsDeviceType)
			//{
			//case GraphicsDeviceType.Direct3D11:
			//	unityGraphicsAPI = MikanClientGraphicsApi.Direct3D11;
			//	break;
			//case GraphicsDeviceType.Direct3D12:
			//	unityGraphicsAPI = MikanClientGraphicsApi.Direct3D12;
			//	break;
			//case GraphicsDeviceType.OpenGLCore:
			//case GraphicsDeviceType.OpenGLES2:
			//case GraphicsDeviceType.OpenGLES3:
			//	unityGraphicsAPI = MikanClientGraphicsApi.OpenGL;
			//	break;
			//default:
			//	Warudo.Core.Utils.Log.Error($"Unsupported graphics API: {SystemInfo.graphicsDeviceType}");
			//	return false;
			//}

			//// Fill out the client info struct and send it to Mikan
			//if (ClientAPI.SetClientInfo(new MikanClientInfo()
			//{
			//	clientId = "Warudo",
			//	engineName = "Unity",
			//	engineVersion = Application.unityVersion,
			//	applicationName = Application.productName,
			//	applicationVersion = Application.version,
			//	xrDeviceName = XRSettings.loadedDeviceName,
			//	graphicsAPI = unityGraphicsAPI,
			//	supportsRGB24 = SystemInfo.SupportsTextureFormat(TextureFormat.RGB24),
			//	supportsRGBA32 = SystemInfo.SupportsTextureFormat(TextureFormat.RGBA32),
			//	supportsBGRA32 = SystemInfo.SupportsTextureFormat(TextureFormat.BGRA32),
			//	supportsDepth = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.Depth)
			//}) != MikanAPIResult.Success)
			//{
			//	Warudo.Core.Utils.Log.Error("Failed to set Mikan client info");
			//	return false;
			//}

			return true;
		}

		private void DisposeMikanAPI()
		{
			ClientAPI.Dispose();
		}

		private void Log(MikanLogLevel logLevel, string logMessage)
		{
			switch (logLevel)
			{
			case MikanLogLevel.Debug:
				Debug.Log($"DEBUG: {logMessage}");
				break;
			case MikanLogLevel.Info:
				Debug.Log($"INFO: {logMessage}");
				break;
			case MikanLogLevel.Warning:
				Debug.LogWarning($"WARNING: {logMessage}");
				break;
			case MikanLogLevel.Error:
				Warudo.Core.Utils.Log.Error($"ERROR: {logMessage}");
				break;
			case MikanLogLevel.Fatal:
				Warudo.Core.Utils.Log.Error($"FATAL: {logMessage}");
				break;
			default:
				Warudo.Core.Utils.Log.Error($"UNKNOWN: {logMessage}");
				break;
			}
		}
	}
}
