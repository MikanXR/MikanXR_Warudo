using Klak.Spout;
using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Core.Plugins;

namespace MikanXR.Warudo.Spike
{
	// Feasibility spike: publishes an animated test pattern through a KlakSpout sender, referencing
	// Warudo's own Klak.Spout.Runtime at compile time rather than reaching it by reflection.
	[PluginType(
		Id = "MikanXR.WarudoSpike",
		Name = "MikanXR Spout Spike",
		Description = "Publishes a test pattern to the Spout sender MikanXR_WarudoSpike.",
		Author = "MikanXR",
		Version = "0.0.1")]
	public class MikanXRSpikePlugin : Plugin
	{
		private const string k_senderName = "MikanXR_WarudoSpike";
		private const string k_blitShaderName = "Hidden/Klak/Spout/Blit";
		private const int k_width = 512;
		private const int k_height = 256;

		[DataInput]
		[Label("Status")]
		public string Status = "";

		private RenderTexture _texture;
		private Texture2D _pattern;
		private GameObject _holder;
		private SpoutSender _sender;

		protected override void OnCreate()
		{
			base.OnCreate();

			SpoutResources resources = FindResources(out string resourcesSource);
			if (resources == null)
			{
				SetStatus("No SpoutResources and no blit shader found");
				return;
			}

			_texture = new RenderTexture(k_width, k_height, 0, RenderTextureFormat.ARGB32)
			{
				name = "MikanXR spike pattern",
				filterMode = FilterMode.Point
			};
			_texture.Create();

			// Four quadrants whose colors cycle, so a receiver shows both orientation and liveness
			_pattern = new Texture2D(2, 2, TextureFormat.RGBA32, false)
			{
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp
			};

			_holder = new GameObject(k_senderName);
			Object.DontDestroyOnLoad(_holder);

			// Configured before it is enabled, so its first capture already sees the texture
			_holder.SetActive(false);
			_sender = _holder.AddComponent<SpoutSender>();
			_sender.SetResources(resources);
			_sender.captureMethod = CaptureMethod.Texture;
			_sender.keepAlpha = true;
			_sender.sourceTexture = _texture;
			_sender.spoutName = k_senderName;
			_holder.SetActive(true);

			SetStatus($"Sending {k_width}x{k_height} as {k_senderName} (resources: {resourcesSource})");
		}

		public override void OnUpdate()
		{
			base.OnUpdate();

			if (_texture == null)
			{
				return;
			}

			float hue = Mathf.Repeat(Time.time * 0.25f, 1.0f);
			_pattern.SetPixel(0, 0, Color.HSVToRGB(hue, 1.0f, 1.0f));
			_pattern.SetPixel(1, 0, Color.white);
			_pattern.SetPixel(0, 1, Color.black);
			_pattern.SetPixel(1, 1, Color.HSVToRGB(Mathf.Repeat(hue + 0.5f, 1.0f), 1.0f, 1.0f));
			_pattern.Apply(false);
			Graphics.Blit(_pattern, _texture);
		}

		protected override void OnDestroy()
		{
			if (_holder != null)
			{
				Object.Destroy(_holder);
			}
			if (_texture != null)
			{
				_texture.Release();
				Object.Destroy(_texture);
			}
			if (_pattern != null)
			{
				Object.Destroy(_pattern);
			}

			base.OnDestroy();
		}

		// Warudo sends its own output through KlakSpout, so its SpoutResources is normally loaded.
		// Failing that, one is built from the blit shader if the player kept it.
		private static SpoutResources FindResources(out string source)
		{
			foreach (SpoutResources candidate in Resources.FindObjectsOfTypeAll<SpoutResources>())
			{
				if (candidate.blitShader != null)
				{
					source = "host asset";
					return candidate;
				}
			}

			Shader blitShader = Shader.Find(k_blitShaderName);
			if (blitShader != null)
			{
				SpoutResources resources = ScriptableObject.CreateInstance<SpoutResources>();
				resources.hideFlags = HideFlags.DontSave;
				resources.blitShader = blitShader;
				source = "Shader.Find";
				return resources;
			}

			source = "none";
			return null;
		}

		private void SetStatus(string status)
		{
			Status = status;
			Debug.Log($"[MikanXRSpike] {status}");
		}
	}
}
