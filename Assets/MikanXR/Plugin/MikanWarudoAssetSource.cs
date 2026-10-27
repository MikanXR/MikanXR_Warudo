using MikanXR.Unity;
using UMod;
using UnityEngine;
using UnityEngine.Rendering;

namespace MikanXR.Warudo
{
	// The package's shaders as the mod carries them: SyncMikanRuntime copies them into the mod
	// folder, and the mod loads them by their project path. A shader name "MikanXR/Foo" is the
	// asset Shaders/MikanFoo.shader. While Warudo runs URP, a shader the mod has a URP form of,
	// ShadersURP/MikanFooURP.shader, loads in its place.
	//
	// Other package assets are not shipped. The Spout resources are a ScriptableObject, which a
	// mod cannot carry, so the writer falls back to the copy Warudo itself has loaded.
	public class MikanWarudoAssetSource : IMikanAssetSource
	{
		private const string k_shaderNamePrefix = "MikanXR/";
		private const string k_shaderFolder = "Assets/MikanXR/Shaders/";
		private const string k_urpShaderFolder = "Assets/MikanXR/ShadersURP/";

		private readonly ModHost _modHost;

		public MikanWarudoAssetSource(ModHost modHost)
		{
			_modHost = modHost;
		}

		public Shader FindShader(string shaderName)
		{
			if (_modHost == null || !shaderName.StartsWith(k_shaderNamePrefix))
			{
				return null;
			}

			// The mod's assets load by project path, or by bare asset name
			string assetName = "Mikan" + shaderName.Substring(k_shaderNamePrefix.Length);
			if (GraphicsSettings.currentRenderPipeline != null)
			{
				Shader urpShader = _modHost.Assets.Load<Shader>(k_urpShaderFolder + assetName + "URP.shader");
				if (urpShader != null)
				{
					return urpShader;
				}
			}

			return _modHost.Assets.Load<Shader>(k_shaderFolder + assetName + ".shader")
				?? _modHost.Assets.Load<Shader>(assetName);
		}

		public T LoadAsset<T>(string resourcesPath) where T : Object
		{
			return null;
		}
	}
}
