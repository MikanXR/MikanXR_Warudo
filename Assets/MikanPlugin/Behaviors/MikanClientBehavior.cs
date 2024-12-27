using UnityEngine;
using Warudo.Core.Utils;
using MikanXR;
using Warudo.Core;

namespace Warudo.Plugins.MikanPlugin.Behaviors
{
	public class MikanClientBehavior : MonoBehaviour
    {
		private MikanAPI clientAPI;
		private MikanEventController eventController;

		private bool wantsToConnect = false;
		private string desiredAddress = "";
		private string desiredPort = "";
		private float reconnectTimer = 0.0f;

		private void Awake()
		{
			var plugin = Context.PluginManager.GetPlugin<MikanXRPlugin>();

			clientAPI = plugin.Client.ClientAPI;
			eventController = plugin.EventController;
		}

		public void Connect(string address, int port)
		{
			if (!clientAPI.GetIsConnected())
			{
				wantsToConnect = true;
				desiredAddress = address;
				desiredPort = port.ToString();
				reconnectTimer = 0.0f;

				AttemptReconnection();
			}
			else
			{
				Log.Error("MikanAPI already connected.");
			}
		}

		protected void AttemptReconnection()
		{
			MikanAPIResult result= clientAPI.Connect(desiredAddress, desiredPort);
			if (result != MikanAPIResult.Success && 
				result != MikanAPIResult.AlreadyConnected)
			{
				// Reset reconnect attempt timer
				reconnectTimer = 1.0f;
				Log.Error("MikanAPI failed to connect to " + desiredAddress + ":" + desiredPort);
			}
			else
			{
				Debug.Log("MikanAPI connected to " + desiredAddress + ":" + desiredPort);
			}
		}

		public void Disconnect()
		{
			wantsToConnect= false;
			desiredAddress = "";
			desiredPort = "";
			reconnectTimer = 0.0f;

			if (clientAPI.GetIsConnected())
			{
				clientAPI.Disconnect();
			}
		}

		public void Update()
		{
			if (clientAPI.GetIsConnected())
			{
				while (clientAPI.FetchNextEvent(out MikanEvent mikanEvent) == MikanAPIResult.Success)
				{
					HandleMikanEvent(mikanEvent);
				}
			}
			else if (wantsToConnect)
			{
				reconnectTimer -= Time.deltaTime;
				if (reconnectTimer <= 0.0f)
				{
					AttemptReconnection();
				}
			}
		}

		public bool IsClientConnected()
		{
			return clientAPI.GetIsConnected();
		}

		public void OnDestroy()
		{
			Disconnect();
		}

		public void HandleMikanEvent(MikanEvent mikanEvent)
		{
			eventController.HandleMikanEvent(mikanEvent);
		}
	}
}
