using Warudo.Core.Events;

namespace MikanXR.Warudo
{
	// Broadcast on Warudo's event bus by the MikanXR asset, for the MikanXR nodes and any other
	// entity that reacts to the editor

	public class MikanXRConnectedEvent : Event
	{
	}

	public class MikanXRDisconnectedEvent : Event
	{
	}

	public class MikanXRSceneChangedEvent : Event
	{
		public int SceneId { get; }
		public string SceneName { get; }

		public MikanXRSceneChangedEvent(int sceneId, string sceneName)
		{
			SceneId = sceneId;
			SceneName = sceneName;
		}
	}
}
