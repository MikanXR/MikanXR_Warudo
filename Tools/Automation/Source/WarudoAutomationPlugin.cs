using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Core.Plugins;

namespace WarudoAutomation
{
	// The Playground plugin that hosts the automation server. Warudo compiles Playground scripts
	// with no mod sandbox, which the server's reflection and file access need, and hot-reloads
	// them on every edit: the old plugin is destroyed and a new one created in the same frame, so
	// the server's listener is closed synchronously here rather than when Unity gets to it.
	//
	// A development tool: anything that can reach the port controls Warudo.
	[PluginType(
		Id = "MikanXR.WarudoAutomation",
		Name = "Warudo Automation",
		Description = "A loopback TCP command channel for automated testing (development only)",
		Author = "MikanXR",
		Version = PluginVersion)]
	public class WarudoAutomationPlugin : Plugin
	{
		public const string PluginVersion = "0.1.0";

		private AutomationServer _server;

		protected override void OnCreate()
		{
			base.OnCreate();

			LogRing.Hook();
			_server = AutomationServer.Create();
			CoreCommands.Register(_server);
			GameObjectCommands.Register(_server);
			ObjectCommands.Register(_server);
			ScreenshotCommands.Register(_server);
			WarudoCommands.Register(_server);

			Debug.Log($"[WarudoAutomation] plugin {PluginVersion} started");
		}

		protected override void OnDestroy()
		{
			if (_server != null)
			{
				_server.Shutdown();
				Object.Destroy(_server.gameObject);
				_server = null;
			}

			LogRing.Unhook();
			base.OnDestroy();
		}
	}
}
