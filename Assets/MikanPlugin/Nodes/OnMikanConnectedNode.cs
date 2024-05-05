using MikanXR;
using Warudo.Core.Attributes;
using Warudo.Core.Graphs;

namespace Warudo.Plugins.MikanPlugin.Nodes {
    [NodeType(Id = "09de12d3-877c-4af9-a7b8-eef319488a8d", Title = "ON_MIKAN_CONNECTED_EVENT", Category = "MIKAN")]
    public class OnMikanConnectedNode : MikanEventNode {
        
        [FlowOutput]
        public Continuation Exit;

        public override string EventType => typeof(MikanConnectedEvent).Name;

        public override void OnMikanEvent(MikanEvent mikanEvent) 
        {
            InvokeFlow(nameof(Exit));
        }
    }
}