using MikanXR.Unity;
using UnityEngine;
using Warudo.Core;
using Warudo.Core.Attributes;
using Warudo.Core.Rendering;
using Warudo.Plugins.Core.Assets;

namespace MikanXR.Warudo
{
	// The MikanXR client in a Warudo scene. Its transform is where the Mikan world sits, so
	// Warudo's gizmo places it, and its data inputs are the client's settings, saved with the
	// scene. The client mirrors the editor's stages, anchors, stencils and cameras under it, and
	// each Mikan camera renders Warudo's content and publishes it to the editor over Spout.
	//
	// The client needs the built-in render pipeline. In Warudo's URP mode it stays disconnected.
	[AssetType(
		Id = "6a1d3c58-2f4e-4b7a-9c61-0e8f5b2d7a43",
		Title = "MikanXR",
		Category = "MikanXR",
		Singleton = true)]
	public class MikanXRAsset : GameObjectAsset
	{
		// The id the editor knows this client by and the prefix of its Spout sender names, which
		// a compositor's client texture source names
		public const string ClientName = "Warudo";

		// Warudo's layers: what the Mikan cameras may render, and what they never should (the UI,
		// gizmos, AvoidRender, and the per-camera layers)
		private const int k_layerDefault = 0;
		private const int k_layerTransparentFX = 1;
		private const int k_layerWater = 4;
		private const int k_layerCharacter = 6;
		private const int k_layerProp = 7;
		private const int k_layerEnvironment = 8;
		private const int k_layerCollidableProp = 9;
		private const int k_layerScreen = 23;

		private const float k_statusIntervalSeconds = 0.5f;

		[Markdown(primary: true)]
		public string Status = "Not connected";

		[Section("Connection")]
		[DataInput]
		[Label("Host")]
		[Description("The editor's address. Blank is this machine.")]
		public string Host = "";

		[DataInput]
		[Label("Port")]
		[Description("The editor's client port. Blank is the default, 8080.")]
		public string Port = "";

		[Trigger]
		[Label("Reconnect")]
		public void Reconnect()
		{
			if (Client != null)
			{
				Client.Disconnect();
				Client.Connect();
			}
		}

		[Section("Scene")]
		[DataInput]
		[Label("World Scale")]
		[Description("Warudo units per Mikan meter. Warudo characters are human sized and the editor's scenes are tabletop sized.")]
		[FloatSlider(0.1f, 50f, 0.1f)]
		public float WorldScale = 10f;

		[Section("Rendering")]
		[DataInput]
		[Label("Render Stencils")]
		[Description("Cut the character out behind the editor's stencils.")]
		public bool RenderStencils = true;

		[DataInput]
		[Label("Render Shadows")]
		[Description("Catch the character's shadow on the stencils and publish the shadow buffer.")]
		public bool RenderShadows = true;

		[DataInput]
		[Label("Render Depth")]
		[Description("Publish the packed depth buffer, for a compositor that keys on depth.")]
		public bool RenderDepth = false;

		[DataInput]
		[Label("Shadow Strength")]
		[FloatSlider(0f, 1f)]
		public float ShadowStrength = 0.7f;

		[DataInput]
		[Label("Render Characters")]
		public bool RenderCharacters = true;

		[DataInput]
		[Label("Render Props")]
		public bool RenderProps = true;

		[DataInput]
		[Label("Render Environment")]
		public bool RenderEnvironment = true;

		[DataInput]
		[Label("Render Screens")]
		public bool RenderScreens = true;

		[Section("Helpers")]
		[DataInput]
		[Label("Show Camera Frustums")]
		public bool ShowCameraFrustums = true;

		[DataInput]
		[Label("Show Stencils")]
		[Description("Draw the stencils as translucent previews in Warudo's own view. The Mikan cameras never see them.")]
		public bool ShowStencils = true;

		[DataInput]
		[Label("Show Light Cones")]
		public bool ShowLightCones = true;

		[Section("Lighting")]
		[DataInput]
		[Label("Apply Light Environment")]
		[Description("Light the scene from the editor's captured light environment.")]
		public bool ApplyLightEnvironment = true;

		[DataInput]
		[Label("Apply Spot Lights")]
		[Description("Mirror the editor's DMX spot lights.")]
		public bool ApplySpotLights = true;

		[DataInput]
		[Label("Light Intensity Scale")]
		[FloatSlider(0f, 4f)]
		public float LightIntensityScale = 1f;

		public MikanClient Client { get; private set; }
		public MikanSceneAnnouncer SceneAnnouncer { get; private set; }
		public bool IsClientConnected => Client != null && Client.IsClientInitialized;

		private bool _clientWanted;
		private float _nextStatusTime;

		protected override GameObject CreateGameObject()
		{
			return new GameObject("MikanXR");
		}

		protected override void OnCreate()
		{
			base.OnCreate();

			WatchAll(new[]
			{
				nameof(Host), nameof(Port), nameof(WorldScale), nameof(RenderStencils), nameof(RenderShadows),
				nameof(RenderDepth), nameof(ShadowStrength), nameof(RenderCharacters), nameof(RenderProps),
				nameof(RenderEnvironment), nameof(RenderScreens), nameof(ShowCameraFrustums), nameof(ShowStencils),
				nameof(ShowLightCones), nameof(ApplyLightEnvironment), nameof(ApplySpotLights), nameof(LightIntensityScale)
			}, ApplySettings);
			Watch(nameof(Host), OnEndpointChanged);
			Watch(nameof(Port), OnEndpointChanged);

			Subscribe<RenderingPipelineChangeEvent>(e => UpdateClientForPipeline());

			// The client starts on the first update, once Warudo has restored the data inputs
			// from the scene, so the settings it reads in Awake are the saved ones
			_clientWanted = true;
		}

		protected override void OnDestroy()
		{
			StopClient();
			base.OnDestroy();
		}

		public override void OnUpdate()
		{
			base.OnUpdate();

			if (_clientWanted)
			{
				_clientWanted = false;
				UpdateClientForPipeline();
			}

			if (Time.unscaledTime >= _nextStatusTime)
			{
				_nextStatusTime = Time.unscaledTime + k_statusIntervalSeconds;
				RefreshStatus();
			}
		}

		// The settings the client starts with, read by the settings provider
		public void CopyTo(MikanSettings settings)
		{
			settings.host = Host;
			settings.port = Port;
			settings.worldScale = WorldScale;
			settings.renderStencils = RenderStencils;
			settings.renderShadows = RenderShadows;
			settings.renderDepth = RenderDepth;
			settings.shadowStrength = ShadowStrength;
			settings.showCameraFrustums = ShowCameraFrustums;
			settings.showStencils = ShowStencils;
			settings.showLightCones = ShowLightCones;
			settings.applyLightEnvironment = ApplyLightEnvironment;
			settings.applySpotLights = ApplySpotLights;
			settings.lightIntensityScale = LightIntensityScale;
			settings.applyRootTransform = false;
		}

		private void UpdateClientForPipeline()
		{
			if (Context.RenderingPipelineManager.CurrentPipeline == RenderingPipeline.BiRP)
			{
				StartClient();
			}
			else
			{
				StopClient();
			}
			RefreshStatus();
		}

		private void StartClient()
		{
			if (Client != null || GameObject == null)
			{
				return;
			}

			// The client reads its settings in Awake, so the object is inactive while it is given
			// its provider and name
			GameObject.SetActive(false);

			Client = GameObject.AddComponent<MikanClient>();
			Client.SettingsProvider = new MikanWarudoSettingsProvider(this);
			Client.SetClientName(ClientName);
			Client.SetRenderCullingMask(ComputeCullingMask());
			Client.Connected += OnClientConnected;
			Client.Disconnected += OnClientDisconnected;

			SceneAnnouncer = GameObject.AddComponent<MikanSceneAnnouncer>();
			SceneAnnouncer.SceneAnnounced += OnSceneAnnounced;

			GameObject.SetActive(true);
			SceneAnnouncer.Bind(Client);

			Debug.Log("[MikanXR] client started");
		}

		private void StopClient()
		{
			if (Client == null)
			{
				return;
			}

			Client.Connected -= OnClientConnected;
			Client.Disconnected -= OnClientDisconnected;
			if (SceneAnnouncer != null)
			{
				SceneAnnouncer.SceneAnnounced -= OnSceneAnnounced;
				Object.Destroy(SceneAnnouncer);
			}

			Object.Destroy(Client);

			// What the client mirrored and its Spout senders live under the root
			if (GameObject != null)
			{
				for (int index = GameObject.transform.childCount - 1; index >= 0; --index)
				{
					Object.Destroy(GameObject.transform.GetChild(index).gameObject);
				}
			}

			Client = null;
			SceneAnnouncer = null;
			SetActive(false);
			Debug.Log("[MikanXR] client stopped");
		}

		private void ApplySettings()
		{
			if (Client == null)
			{
				return;
			}

			Client.SetWorldScale(WorldScale);
			Client.SetRenderStencils(RenderStencils);
			Client.SetRenderShadows(RenderShadows);
			Client.SetRenderDepth(RenderDepth);
			Client.SetShadowStrength(ShadowStrength);
			Client.SetShowCameraFrustums(ShowCameraFrustums);
			Client.SetShowStencils(ShowStencils);
			Client.SetShowLightCones(ShowLightCones);
			Client.SetApplyLightEnvironment(ApplyLightEnvironment);
			Client.SetApplySpotLights(ApplySpotLights);
			Client.SetLightIntensityScale(LightIntensityScale);
			Client.SetRenderCullingMask(ComputeCullingMask());
		}

		// A new address takes effect on a fresh connection
		private void OnEndpointChanged()
		{
			if (Client == null)
			{
				return;
			}

			Client.Settings.host = Host;
			Client.Settings.port = Port;
			Reconnect();
		}

		private int ComputeCullingMask()
		{
			int mask = (1 << k_layerDefault) | (1 << k_layerTransparentFX) | (1 << k_layerWater);
			if (RenderCharacters)
			{
				mask |= 1 << k_layerCharacter;
			}
			if (RenderProps)
			{
				mask |= (1 << k_layerProp) | (1 << k_layerCollidableProp);
			}
			if (RenderEnvironment)
			{
				mask |= 1 << k_layerEnvironment;
			}
			if (RenderScreens)
			{
				mask |= 1 << k_layerScreen;
			}
			return mask;
		}

		private void OnClientConnected()
		{
			SetActive(true);
			Context.EventBus.Broadcast(new MikanXRConnectedEvent());
		}

		private void OnClientDisconnected()
		{
			SetActive(false);
			Context.EventBus.Broadcast(new MikanXRDisconnectedEvent());
		}

		private void OnSceneAnnounced(int sceneId, string sceneName)
		{
			Debug.Log($"[MikanXR] scene changed to '{sceneName}' ({sceneId})");
			Context.EventBus.Broadcast(new MikanXRSceneChangedEvent(sceneId, sceneName));
		}

		private void RefreshStatus()
		{
			string status;
			if (Context.RenderingPipelineManager.CurrentPipeline != RenderingPipeline.BiRP)
			{
				status = "MikanXR needs Warudo's built-in render pipeline. It stays disconnected in URP mode.";
			}
			else if (Client == null)
			{
				status = "Starting";
			}
			else if (Client.IsClientInitialized)
			{
				string host = string.IsNullOrEmpty(Client.Settings.host) ? "this machine" : Client.Settings.host;
				status = $"Connected to {host} as {Client.API.GetClientUniqueID()}";
			}
			else
			{
				status = Client.IsConnected ? "Connecting" : "Not connected (retrying)";
			}

			if (status != Status)
			{
				Status = status;
				BroadcastDataInput(nameof(Status));
			}
		}
	}
}
