using MikanXR;
using Warudo.Core;
using Warudo.Core.Attributes;
using Warudo.Core.Graphs;
using Warudo.Core.Localization;
using Warudo.Plugins.MikanPlugin;

namespace MikanXR.Nodes
{
	public abstract class MikanEventNode : Node, IMikanEventHandler
    {
		protected MikanEventController EventController => 
			Context.PluginManager.GetPlugin<MikanXRPlugin>().EventController;

		public virtual string EventType => throw new System.NotImplementedException();

		protected override void OnCreate()
		{
			base.OnCreate();
			EventController.SubscribeEvent(this);
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();
			EventController.UnsubscribeEvent(this);
		}

		public virtual void OnMikanEvent(MikanEvent mikanEvent)
		{
			throw new System.NotImplementedException();
		}
	}
}