
using Warudo.Core.Attributes;
using Warudo.Core.Plugins;
using Warudo.Plugins.Core;
using MikanXR.Nodes;
using Warudo.Core;
using UnityEngine;

namespace MikanXR {
    [PluginType(
        Id = "Warudo.MikanXR",
        Name = "MIKAN_XR",
        Description = "MIKAN_XR_DESCRIPTION",
        Author = "MikanXR Org",
        Version = "1.0.0",
         NodeTypes = new[] {
             typeof(OnMikanConnectedNode)
         })]
    public class MikanXRPlugin : Plugin {

		//TODO: Where do these get initialized from?
		public Material modelStencilMaterial;
		public GameObject anchorPrefab;
		public GameObject stencilPrefab;

		public float DefaultSceneScale = 1.0f;

		private MikanSettings _settings = null;
		public MikanSettings Settings => _settings;

		private GameObject _mikanManagerGO = null;
		private MikanManager _mikanManager = null;
		public MikanManager Manager => _mikanManager;

		private MikanClient _mikanClient = null;
		public MikanClient Client => _mikanClient;

		private MikanLogger _logger = null;
		public MikanLogger Logger => _logger;

		public MikanEventController EventController { get; } = new();

		private bool _isMikanInitialized = false;

		protected override void OnCreate() 
        {
			Log(MikanLogLevel.Info, "MikanXRPlugin - Awake Called");

			// Load the settings
			InitSettings();

			// Setup MikanManager, Client and Logger
			if (!SetupMikan())
			{
				TearDownMikan();
			}
		}

		protected void InitSettings()
		{
			// TODO: Load settings from Warudo (override Serialize() method?)
			_settings = new MikanSettings();
			if (!_settings.LoadSettings())
			{
				_settings.SceneScale = DefaultSceneScale;
			}
		}

		private bool SetupMikan()
		{
			Log(MikanLogLevel.Info, "MikanXRPlugin - SetupMikan Called");

			if (!_isMikanInitialized)
			{
				// Create the MikanManager
				// TODO: Is it safe to create a new GameObject at this point?
				_mikanManagerGO = new GameObject(
					"MikanManagerObject",
					new System.Type[] {
						typeof(MikanManager)
					});
				_mikanManager = _mikanManagerGO.GetComponent<MikanManager>();
				_mikanManager.Setup(this);

				// Cache the MikanClient that the MikanManager created
				_mikanClient = _mikanManager.MikanClient;

				// Register the logger callback
				_logger = _mikanClient.ClientAPI.CoreAPI.Logger;
				_logger.AddLogCallback(Log);

				_isMikanInitialized = true;
			}

			return _isMikanInitialized;
		}

		protected override void OnDestroy() 
		{
            base.OnDestroy();

			Log(MikanLogLevel.Info, "MikanXRPlugin - OnDestroy Called");

			TearDownMikan();
		}

		public override void OnApplicationQuit()
		{
			Log(MikanLogLevel.Info, "MikanXRPlugin - OnApplicationQuit Called");

			TearDownMikan();
		}

		private void TearDownMikan()
		{
			Log(MikanLogLevel.Info, "MikanXRPlugin - TearDownMikan Called");

			if (_isMikanInitialized)
			{
				if (_mikanManager != null)
				{
					_mikanManager.TearDown();
				}

				if (_logger != null)
				{
					_logger.RemoveLogCallback(Log);
					_logger = null;
				}

				_mikanClient = null;
				_isMikanInitialized = false;
			}
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
