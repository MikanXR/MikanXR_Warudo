
using Warudo.Core.Attributes;
using Warudo.Core.Plugins;
using Warudo.Plugins.Core;
using Warudo.Plugins.MikanPlugin.Nodes;
using MikanXR;
using Warudo.Core;

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
        }
        protected override void OnDestroy() {
            base.OnDestroy();
            MikanClientAPI.Shutdown();
        }
        
    }
}
