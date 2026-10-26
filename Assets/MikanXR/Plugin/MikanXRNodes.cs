using System.Linq;
using Warudo.Core;
using Warudo.Core.Attributes;
using Warudo.Core.Graphs;

namespace MikanXR.Warudo
{
	// Blueprint nodes that follow the editor through the MikanXR asset's events

	[NodeType(
		Id = "0b7e3f2a-5c91-4d68-9e4a-1f6d8c2b7a35",
		Title = "On MikanXR Connected",
		Category = "MikanXR")]
	public class OnMikanXRConnectedNode : Node
	{
		[FlowOutput]
		public Continuation Exit;

		protected override void OnCreate()
		{
			base.OnCreate();
			Subscribe<MikanXRConnectedEvent>(e => InvokeFlow(nameof(Exit)));
		}
	}

	[NodeType(
		Id = "8d2c6a91-3e7f-4b05-a1d9-6c4e2f8b0d17",
		Title = "On MikanXR Disconnected",
		Category = "MikanXR")]
	public class OnMikanXRDisconnectedNode : Node
	{
		[FlowOutput]
		public Continuation Exit;

		protected override void OnCreate()
		{
			base.OnCreate();
			Subscribe<MikanXRDisconnectedEvent>(e => InvokeFlow(nameof(Exit)));
		}
	}

	// Fires when the editor's current scene changes, and once when the client connects and
	// learns it, with the scene's id and name
	[NodeType(
		Id = "e5a1f9c3-2b6d-4e87-9f30-7d1c5b8a4e62",
		Title = "On MikanXR Scene Changed",
		Category = "MikanXR")]
	public class OnMikanXRSceneChangedNode : Node
	{
		[FlowOutput]
		public Continuation Exit;

		[DataOutput]
		[Label("Scene Id")]
		public int SceneId() => _sceneId;

		[DataOutput]
		[Label("Scene Name")]
		public string SceneName() => _sceneName;

		private int _sceneId = -1;
		private string _sceneName = "";

		protected override void OnCreate()
		{
			base.OnCreate();
			Subscribe<MikanXRSceneChangedEvent>(e =>
			{
				_sceneId = e.SceneId;
				_sceneName = e.SceneName;
				InvokeFlow(nameof(Exit));
			});
		}
	}

	// The editor's current scene and whether the client is connected, for a graph that reads
	// them outside the events
	[NodeType(
		Id = "4f8b2d6e-9a13-4c7f-b5e0-2d7a9c1e6f48",
		Title = "Get MikanXR Scene",
		Category = "MikanXR")]
	public class GetMikanXRSceneNode : Node
	{
		[DataOutput]
		[Label("Connected")]
		public bool Connected() => FindAsset()?.IsClientConnected ?? false;

		[DataOutput]
		[Label("Scene Id")]
		public int SceneId() => FindAsset()?.SceneAnnouncer?.CurrentSceneId ?? -1;

		[DataOutput]
		[Label("Scene Name")]
		public string SceneName() => FindAsset()?.SceneAnnouncer?.CurrentSceneName ?? "";

		private static MikanXRAsset FindAsset()
		{
			return Context.OpenedScene?.GetAssets<MikanXRAsset>().FirstOrDefault();
		}
	}
}
