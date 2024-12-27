using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MikanXR {
    public interface IMikanEventHandler
    {
        public string EventType { get; }
        public void OnMikanEvent(MikanEvent mikanEvent);
    }
}