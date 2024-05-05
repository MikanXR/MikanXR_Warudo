using MikanXR;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Warudo.Plugins.MikanPlugin {
    public interface IMikanEventHandler
    {
        public string EventType { get; }
        public void OnMikanEvent(MikanEvent mikanEvent);
    }
}