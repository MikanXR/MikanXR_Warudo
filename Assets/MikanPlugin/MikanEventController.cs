using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MikanXR {
    public class MikanEventController
    {
        private Dictionary<string, HashSet<IMikanEventHandler>> _eventHandlers = new();

        public void SubscribeEvent(IMikanEventHandler eventHandler)
        {
            if (_eventHandlers.TryGetValue(eventHandler.EventType, out var handlers))
            {
				handlers.Add(eventHandler);
			}
			else
            {
				_eventHandlers[eventHandler.EventType] = new HashSet<IMikanEventHandler> { eventHandler };
			}
        }

        public void UnsubscribeEvent(IMikanEventHandler eventHandler)
        {
			if (_eventHandlers.TryGetValue(eventHandler.EventType, out var handlers))
            {
                handlers.Remove(eventHandler);

                if (handlers.Count == 0)
                {
                    _eventHandlers.Remove(eventHandler.EventType);
                }
            }
		}

        public void HandleMikanEvent(MikanEvent mikanEvent)
        {
			if (_eventHandlers.TryGetValue(mikanEvent.eventTypeName, out var handlers))
            {
				foreach (var handler in handlers)
                {
					handler.OnMikanEvent(mikanEvent);
				}
			}
		}
    }
}
