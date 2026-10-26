using Cysharp.Threading.Tasks;
using MikanXR.Unity;
using System.Linq;
using UnityEngine;
using Warudo.Core;
using Warudo.Core.Attributes;
using Warudo.Core.Data;
using Warudo.Plugins.Core.Assets;

namespace MikanXR.Warudo
{
	// Places one of the editor's stages in the Warudo scene. While the anchor exists and its stage
	// is mirrored, the stage sits where the anchor is, moved with Warudo's gizmo, and the editor's
	// own transform for the stage is ignored. Removing the anchor or clearing its stage hands the
	// stage back to the editor's transform.
	[AssetType(
		Id = "c2f84e1b-7d3a-4f59-a8e6-3b9d0c5e1f72",
		Title = "MikanXR Stage Anchor",
		Category = "MikanXR")]
	public class MikanXRStageAnchorAsset : GameObjectAsset
	{
		[Markdown(primary: true)]
		public string Status = "No stage chosen";

		[DataInput]
		[Label("Stage")]
		[Description("The editor stage this anchor places.")]
		[AutoComplete(nameof(AutoCompleteStage))]
		public string StageName = "";

		private MikanStageObject _pinnedStage;
		private Matrix4x4 _lastAppliedPose;

		protected override GameObject CreateGameObject()
		{
			return new GameObject("MikanXR Stage Anchor");
		}

		protected override void OnCreate()
		{
			base.OnCreate();
			Watch(nameof(StageName), ReleaseStage);
			SetActive(true);
		}

		protected override void OnDestroy()
		{
			ReleaseStage();
			base.OnDestroy();
		}

		public override void OnLateUpdate()
		{
			base.OnLateUpdate();

			MikanStageObject stage = FindStage();
			if (stage == null)
			{
				SetStatus(string.IsNullOrEmpty(StageName) ? "No stage chosen" : $"Waiting for stage '{StageName}' from the editor");
				return;
			}

			// Re-applied when the anchor moves, and when the stage object is new (a reconnect or a
			// scene reload respawns it)
			Matrix4x4 pose = GameObject.transform.localToWorldMatrix;
			if (stage == _pinnedStage && pose == _lastAppliedPose)
			{
				return;
			}

			Transform anchor = GameObject.transform;
			stage.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
			stage.transform.localScale = anchor.localScale;
			stage.PinHere();

			_pinnedStage = stage;
			_lastAppliedPose = pose;
			SetStatus($"Placing stage '{StageName}'");
		}

		protected async UniTask<AutoCompleteList> AutoCompleteStage()
		{
			MikanClient client = FindClient();
			if (client == null || !client.IsClientInitialized)
			{
				return AutoCompleteList.Message("Add a MikanXR asset and connect to the editor first");
			}

			return AutoCompleteList.Single(client.SceneMirror.FindAll<MikanStageObject>()
				.Select(stage => stage.StageName)
				.Where(name => !string.IsNullOrEmpty(name))
				.Distinct()
				.Select(name => new AutoCompleteEntry { label = name, value = name }));
		}

		private static MikanClient FindClient()
		{
			return Context.OpenedScene?.GetAssets<MikanXRAsset>().FirstOrDefault()?.Client;
		}

		private MikanStageObject FindStage()
		{
			if (string.IsNullOrEmpty(StageName))
			{
				return null;
			}

			MikanClient client = FindClient();
			if (client == null || !client.IsClientInitialized)
			{
				return null;
			}

			return client.SceneMirror.FindAll<MikanStageObject>().FirstOrDefault(stage => stage.StageName == StageName);
		}

		// Hands the stage the anchor placed back to the editor's transform
		private void ReleaseStage()
		{
			if (_pinnedStage != null)
			{
				_pinnedStage.Unpin();
				_pinnedStage = null;
			}
			_lastAppliedPose = Matrix4x4.zero;
		}

		private void SetStatus(string status)
		{
			if (status != Status)
			{
				Status = status;
				BroadcastDataInput(nameof(Status));
			}
		}
	}
}
