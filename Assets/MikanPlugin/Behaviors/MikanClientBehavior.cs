using System;
using System.Collections.Generic;
using UnityEngine;
using Warudo.Core.Data;
using Warudo.Core.Data.Models;
using Warudo.Core.Utils;
using MikanXR;

namespace Warudo.Plugins.MikanPlugin.Behaviors
{
	public class MikanClientBehavior : MonoBehaviour
    {
		private MikanAPI clientAPI;

		private void Awake()
		{
			clientAPI= new MikanXR.MikanAPI();
			clientAPI.Initialize(MikanLogLevel.Info);
		}

		public bool IsClientConnected()
		{
			return clientAPI.GetIsConnected();
		}

		public void Connect(string address, int port)
		{
			clientAPI.Connect(address, port.ToString());
		}

		public void Disconnect()
		{
			clientAPI.Disconnect();
		}

		public void OnDestroy()
		{
			clientAPI.Disconnect();
			clientAPI.Shutdown();
		}
	}
}
