using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Core.Localization;
using Warudo.Core.Scenes;
using Warudo.Plugins.MikanPlugin.Behaviors;
using Object = UnityEngine.Object;

namespace Warudo.Plugins.MikanPlugin.Assets
{
	[AssetType(Id = "1601beed-b5fd-4ebd-b271-da5249f0c9d5", Title = "MIKAN_CLIENT", Category = "CATEGORY_MIKAN")]
	public class MikanClientAsset : Asset
	{
		[Markdown(order: -5000)]
		public string Status = "MIKAN_CLIENT_NOT_CONNECTED".Localized();

		[DataInput(order: -999)]
		[Label("Address")]
		public string Address = "ws://127.0.0.1";

		[DataInput(order: -999)]
		[Label("PORT")]
		public int Port = 8080;

		private MikanClientBehavior mikanClientBehavior;

		protected override void OnCreate()
		{
			base.OnCreate();
			var gameObject = new GameObject("Mikan Client");
			mikanClientBehavior = gameObject.AddComponent<MikanClientBehavior>();
			Watch(nameof(Address), ResetConnection);
			Watch(nameof(Port), ResetConnection);
			ResetConnection();
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();
			if (mikanClientBehavior != null)
			{
				Object.Destroy(mikanClientBehavior.gameObject);
				mikanClientBehavior = null;
			}
		}

		public void ResetConnection()
		{
			mikanClientBehavior.Disconnect();
			mikanClientBehavior.Connect(Address, Port);
		}
	}
}