using MikanXR.Unity;

namespace MikanXR.Warudo
{
	// The client's settings as the MikanXR asset's data inputs, which Warudo saves with the scene.
	// The data inputs are the source of truth: the client only reads them. Stage placements are
	// owned by the stage anchor assets, so they are not stored here either.
	public class MikanWarudoSettingsProvider : IMikanSettingsProvider
	{
		private readonly MikanXRAsset _asset;

		public MikanWarudoSettingsProvider(MikanXRAsset asset)
		{
			_asset = asset;
		}

		public bool Load(MikanSettings settings)
		{
			_asset.CopyTo(settings);
			return true;
		}

		public void Save(MikanSettings settings)
		{
		}
	}
}
