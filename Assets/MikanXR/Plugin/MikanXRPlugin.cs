using MikanXR.Unity;
using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Core.Plugins;

namespace MikanXR.Warudo
{
	// The mod's root. It registers the MikanXR asset and nodes and points the client's shader
	// lookup at the shaders the mod carries. The client itself lives on the MikanXR scene asset.
	[PluginType(
		Id = "MikanXR.Warudo",
		Name = "MikanXR",
		Description = "Mirrors a MikanXR editor into Warudo and renders the character into its calibrated cameras",
		Author = "MikanXR",
		Version = "0.1.0",
		AssetTypes = new[] { typeof(MikanXRAsset), typeof(MikanXRStageAnchorAsset) },
		NodeTypes = new[]
		{
			typeof(OnMikanXRConnectedNode), typeof(OnMikanXRDisconnectedNode), typeof(OnMikanXRSceneChangedNode),
			typeof(GetMikanXRSceneNode)
		})]
	public class MikanXRPlugin : Plugin
	{
		protected override void OnCreate()
		{
			base.OnCreate();

			MikanShaderLibrary.SetAssetSource(new MikanWarudoAssetSource(ModHost));
			Debug.Log("[MikanXR] Warudo plugin started");
		}

		protected override void OnDestroy()
		{
			MikanShaderLibrary.SetAssetSource(null);
			base.OnDestroy();
		}
	}
}
