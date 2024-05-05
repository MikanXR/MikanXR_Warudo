
using Warudo.Core.Attributes;
using Warudo.Core.Plugins;
using Warudo.Plugins.Core;
using Warudo.Plugins.MikanXR.Nodes;
using MikanXR;

namespace Warudo.Plugins.StreamDeck {
    [PluginType(
        Id = "Warudo.MikanXR",
        Name = "MIKAN_XR",
        Description = "STREAM_DECK_DESCRIPTION",
        Author = "MikanXR Org",
        Version = "1.0.0",
        NodeTypes = new[] {
            typeof(OnMikanConnectedNode),
        })]
    public class MikanXRPlugin : Plugin {
        
        public MikanAPI MikanClientAPI { get; } = new();

        protected override void OnCreate() {
            var corePlugin = Context.PluginManager.GetPlugin<CorePlugin>();
            corePlugin.RegisterCharacterTrackingTemplate<VMCFaceTrackingTemplate>(this);
            corePlugin.RegisterCharacterTrackingTemplate<VMCPoseTrackingTemplate>(this);
        }
        protected override void OnDestroy() {
            base.OnDestroy();
            MikanClientAPI.Shutdown();
        }
        
    }
}
