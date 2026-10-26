using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Warudo.Core;
using Warudo.Core.Data;
using Warudo.Core.Graphs;
using Warudo.Core.Plugins;
using Warudo.Core.Rendering;
using Warudo.Core.Scenes;

namespace WarudoAutomation
{
	// warudo: Warudo's own entities. Assets, plugins and graph nodes are all entities whose
	// settings are data input ports and whose buttons are trigger ports, so one set of verbs
	// reads, writes and presses them. Writes go through Warudo's setters, which run the entity's
	// watchers and update the editor UI the same as a user edit.
	public static class WarudoCommands
	{
		public static void Register(AutomationServer server)
		{
			server.RegisterNamespace("warudo", new[]
			{
				"warudo assets",
				"warudo asset types [filter]",
				"warudo asset create <typeTitle|typeId> [name]",
				"warudo asset destroy <asset>",
				"warudo asset ports <asset>",
				"warudo asset get <asset> <port.path>",
				"warudo asset set <asset> <port.path> <value...>",
				"warudo asset setjson <asset> <port.path> <json...>",
				"warudo asset trigger <asset> <trigger>",
				"warudo plugins",
				"warudo plugin ports|get|set|setjson|trigger <pluginId> ...",
				"warudo plugin save",
				"warudo scene info",
				"warudo scene save",
				"warudo pipeline get",
				"warudo pipeline set <builtin|urp>",
				"warudo graphs",
				"warudo graph create <name>",
				"warudo graph destroy <graph>",
				"warudo graph enable <graph> <true|false>",
				"warudo graph nodes <graph>",
				"warudo graph node types [filter]",
				"warudo graph node add <graph> <nodeTitle|nodeTypeId>",
				"warudo graph node ports|get|set|setjson|trigger <graph> <node> ...",
				"warudo graph flow <graph> <fromNode> <fromPort> <toNode> <toPort>",
				"warudo graph data <graph> <fromNode> <fromPort> <toNode> <toPort>",
			}, Handle);
		}

		// An asset by id, name or type title, the same lookup the $<asset> target uses
		public static bool TryFindAsset(string token, out Asset asset, out string error)
		{
			asset = null;
			error = null;

			Scene scene = Context.OpenedScene;
			if (scene == null)
			{
				error = "no Warudo scene is open";
				return false;
			}

			if (Guid.TryParse(token, out Guid id))
			{
				asset = scene.GetAsset(id);
			}

			asset ??= scene.GetAssetList().FirstOrDefault(a => a.Name == token)
				?? scene.GetAssetList().FirstOrDefault(a => TypeTitle(a) == token);

			if (asset == null)
			{
				error = $"no asset '{token}' in the scene";
				return false;
			}
			return true;
		}

		private static bool Handle(CommandRequest request, List<string> outLines, out string outError)
		{
			outError = null;
			switch (request.Verb)
			{
				case "assets":
					return ListAssets(outLines, out outError);
				case "asset":
					return HandleAsset(request, outLines, out outError);
				case "plugins":
					foreach (Plugin plugin in Context.PluginManager.GetPlugins())
					{
						outLines.Add($"{plugin.PluginId} {plugin.GetType().FullName}");
					}
					return true;
				case "plugin":
					return HandlePlugin(request, outLines, out outError);
				case "scene":
					return HandleScene(request, outLines, out outError);
				case "pipeline":
					return HandlePipeline(request, outLines, out outError);
				case "graphs":
					return ListGraphs(outLines, out outError);
				case "graph":
					return HandleGraph(request, outLines, out outError);
			}

			outError = "usage: warudo assets|asset|plugins|plugin|scene|pipeline|graphs|graph";
			return false;
		}

		// -- Assets -----

		private static bool ListAssets(List<string> outLines, out string outError)
		{
			outError = null;
			Scene scene = Context.OpenedScene;
			if (scene == null)
			{
				outError = "no Warudo scene is open";
				return false;
			}

			foreach (Asset asset in scene.GetAssetList())
			{
				outLines.Add($"{asset.Id} \"{TypeTitle(asset)}\" \"{asset.Name}\" {(asset.Active ? "active" : "inactive")}");
			}
			return true;
		}

		private static bool HandleAsset(CommandRequest request, List<string> outLines, out string outError)
		{
			outError = null;
			string sub = request.Args.Count > 1 ? request.Args[1] : "";

			if (sub == "types")
			{
				string filter = request.Args.Count > 2 ? request.Args[2] : null;
				foreach (AssetTypeMeta meta in Context.AssetTypeRegistry.GetRegisteredTypes().Values)
				{
					string title = AssetTitle(meta.Type);
					if (filter == null || title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
						meta.Type.FullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						outLines.Add($"{meta.Id} \"{title}\" {meta.Type.FullName}");
					}
				}
				return true;
			}

			if (sub == "create")
			{
				if (request.Args.Count < 3)
				{
					outError = "usage: warudo asset create <typeTitle|typeId> [name]";
					return false;
				}

				Scene scene = Context.OpenedScene;
				if (scene == null)
				{
					outError = "no Warudo scene is open";
					return false;
				}

				string typeToken = request.Args[2];
				AssetTypeMeta meta = Context.AssetTypeRegistry.GetRegisteredTypes().Values.FirstOrDefault(m =>
					m.Id == typeToken || AssetTitle(m.Type) == typeToken || m.Type.Name == typeToken);
				if (meta == null)
				{
					outError = $"no asset type '{typeToken}' (see warudo asset types)";
					return false;
				}

				Asset created = scene.AddAsset(meta.Id);
				if (request.Args.Count > 3)
				{
					created.Name = string.Join(" ", request.Args.Skip(3));
				}
				outLines.Add($"{created.Id} \"{TypeTitle(created)}\" \"{created.Name}\"");
				return true;
			}

			if (request.Args.Count < 3)
			{
				outError = "usage: warudo asset <destroy|ports|get|set|setjson|trigger> <asset> ...";
				return false;
			}

			if (!TryFindAsset(request.Args[2], out Asset asset, out outError))
			{
				return false;
			}

			if (sub == "destroy")
			{
				Context.OpenedScene.RemoveAsset(asset.Id);
				return true;
			}

			return HandleEntityVerb(asset, sub, request, 3, outLines, out outError);
		}

		// -- Plugins -----

		private static bool HandlePlugin(CommandRequest request, List<string> outLines, out string outError)
		{
			outError = null;

			// Plugin settings are written when Warudo saves them, which a scripted quit skips
			if (request.Args.Count > 1 && request.Args[1] == "save")
			{
				Context.PluginManager.SavePluginData();
				outLines.Add("saved");
				return true;
			}

			if (request.Args.Count < 3)
			{
				outError = "usage: warudo plugin <ports|get|set|setjson|trigger> <pluginId> ... | warudo plugin save";
				return false;
			}

			Plugin plugin = Context.PluginManager.GetPlugin(request.Args[2]);
			if (plugin == null)
			{
				outError = $"no plugin '{request.Args[2]}' (see warudo plugins)";
				return false;
			}

			return HandleEntityVerb(plugin, request.Args[1], request, 3, outLines, out outError);
		}

		// -- Scene and pipeline -----

		private static bool HandleScene(CommandRequest request, List<string> outLines, out string outError)
		{
			outError = null;
			Scene scene = Context.OpenedScene;
			if (scene == null)
			{
				outError = "no Warudo scene is open";
				return false;
			}

			switch (request.Args.Count > 1 ? request.Args[1] : "")
			{
				case "info":
					outLines.Add($"name {scene.Name}");
					outLines.Add($"assets {scene.GetAssetList().Count}");
					outLines.Add($"graphs {scene.GetGraphList().Count}");
					return true;

				case "save":
				{
					DeferredReply reply = AutomationServer.Instance.DeferReply();
					SaveScene(scene, reply);
					return true;
				}
			}

			outError = "usage: warudo scene info|save";
			return false;
		}

		private static async void SaveScene(Scene scene, DeferredReply reply)
		{
			try
			{
				await scene.Save();
				reply.Send(new List<string> { $"saved {scene.Name}" });
			}
			catch (Exception e)
			{
				reply.Fail(AutomationServer.DescribeException(e));
			}
		}

		private static bool HandlePipeline(CommandRequest request, List<string> outLines, out string outError)
		{
			outError = null;
			RenderingPipelineManager manager = Context.RenderingPipelineManager;

			switch (request.Args.Count > 1 ? request.Args[1] : "")
			{
				case "get":
					outLines.Add(manager.CurrentPipeline.ToString());
					return true;

				case "set":
				{
					string wanted = request.Args.Count > 2 ? request.Args[2].ToLowerInvariant() : "";
					RenderingPipeline pipeline;
					if (wanted == "builtin" || wanted == "birp")
					{
						pipeline = RenderingPipeline.BiRP;
					}
					else if (wanted == "urp")
					{
						pipeline = RenderingPipeline.URP;
					}
					else
					{
						outError = "usage: warudo pipeline set <builtin|urp>";
						return false;
					}

					manager.SetPipeline(pipeline);
					outLines.Add(manager.CurrentPipeline.ToString());
					return true;
				}
			}

			outError = "usage: warudo pipeline get|set <builtin|urp>";
			return false;
		}

		// -- Graphs -----

		private static bool ListGraphs(List<string> outLines, out string outError)
		{
			outError = null;
			Scene scene = Context.OpenedScene;
			if (scene == null)
			{
				outError = "no Warudo scene is open";
				return false;
			}

			foreach (Graph graph in scene.GetGraphList())
			{
				outLines.Add($"{graph.Id} \"{graph.Name}\" {(graph.Enabled ? "enabled" : "disabled")} nodes={graph.GetNodes().Count}");
			}
			return true;
		}

		private static bool TryFindGraph(string token, out Graph graph, out string error)
		{
			graph = null;
			error = null;
			Scene scene = Context.OpenedScene;
			if (scene == null)
			{
				error = "no Warudo scene is open";
				return false;
			}

			if (Guid.TryParse(token, out Guid id))
			{
				graph = scene.GetGraph(id);
			}
			graph ??= scene.GetGraphList().FirstOrDefault(g => g.Name == token);

			if (graph == null)
			{
				error = $"no graph '{token}'";
				return false;
			}
			return true;
		}

		private static bool TryFindNode(Graph graph, string token, out Node node, out string error)
		{
			node = null;
			error = null;
			if (Guid.TryParse(token, out Guid id))
			{
				node = graph.GetNode(id);
			}
			node ??= graph.GetNodes().Values.FirstOrDefault(n => NodeTitle(n) == token);

			if (node == null)
			{
				error = $"no node '{token}' in graph '{graph.Name}'";
				return false;
			}
			return true;
		}

		private static bool HandleGraph(CommandRequest request, List<string> outLines, out string outError)
		{
			outError = null;
			string sub = request.Args.Count > 1 ? request.Args[1] : "";

			switch (sub)
			{
				case "create":
				{
					if (request.Args.Count < 3)
					{
						outError = "usage: warudo graph create <name>";
						return false;
					}
					var graph = new Graph { Name = request.RawRest(2), Enabled = true };
					Context.OpenedScene.AddGraph(graph);
					outLines.Add($"{graph.Id} \"{graph.Name}\"");
					return true;
				}

				case "destroy":
				{
					if (request.Args.Count < 3 || !TryFindGraph(request.Args[2], out Graph graph, out outError))
					{
						outError ??= "usage: warudo graph destroy <graph>";
						return false;
					}
					Context.OpenedScene.RemoveGraph(graph.Id);
					return true;
				}

				case "enable":
				{
					if (request.Args.Count < 4 || !TryFindGraph(request.Args[2], out Graph graph, out outError) ||
						!bool.TryParse(request.Args[3], out bool enabled))
					{
						outError ??= "usage: warudo graph enable <graph> <true|false>";
						return false;
					}
					graph.Enabled = enabled;
					outLines.Add(graph.Enabled ? "enabled" : "disabled");
					return true;
				}

				case "nodes":
				{
					if (request.Args.Count < 3 || !TryFindGraph(request.Args[2], out Graph graph, out outError))
					{
						outError ??= "usage: warudo graph nodes <graph>";
						return false;
					}
					foreach (Node node in graph.GetNodes().Values)
					{
						outLines.Add($"{node.Id} \"{NodeTitle(node)}\" {node.GetType().FullName}");
					}
					return true;
				}

				case "node":
					return HandleGraphNode(request, outLines, out outError);

				case "flow":
				case "data":
				{
					if (request.Args.Count < 7 || !TryFindGraph(request.Args[2], out Graph graph, out outError) ||
						!TryFindNode(graph, request.Args[3], out Node from, out outError) ||
						!TryFindNode(graph, request.Args[5], out Node to, out outError))
					{
						outError ??= $"usage: warudo graph {sub} <graph> <fromNode> <fromPort> <toNode> <toPort>";
						return false;
					}

					bool connected = sub == "flow"
						? graph.AddFlowConnection(from, request.Args[4], to, request.Args[6])
						: graph.AddDataConnection(from, request.Args[4], to, request.Args[6]);
					if (!connected)
					{
						outError = "Warudo refused the connection";
						return false;
					}
					return true;
				}
			}

			outError = "usage: warudo graph create|destroy|enable|nodes|node|flow|data";
			return false;
		}

		private static bool HandleGraphNode(CommandRequest request, List<string> outLines, out string outError)
		{
			outError = null;
			string sub = request.Args.Count > 2 ? request.Args[2] : "";

			if (sub == "types")
			{
				string filter = request.Args.Count > 3 ? request.Args[3] : null;
				foreach (var meta in Context.NodeTypeRegistry.GetRegisteredTypes().Values)
				{
					string title = NodeTitle(meta.Type);
					if (filter == null || title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
						meta.Type.FullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						outLines.Add($"{meta.Id} \"{title}\" {meta.Type.FullName}");
					}
				}
				return true;
			}

			if (request.Args.Count < 4 || !TryFindGraph(request.Args[3], out Graph graph, out outError))
			{
				outError ??= "usage: warudo graph node <types|add|ports|get|set|setjson|trigger> <graph> ...";
				return false;
			}

			if (sub == "add")
			{
				if (request.Args.Count < 5)
				{
					outError = "usage: warudo graph node add <graph> <nodeTitle|nodeTypeId>";
					return false;
				}

				string typeToken = request.RawRest(4);
				var meta = Context.NodeTypeRegistry.GetRegisteredTypes().Values.FirstOrDefault(m =>
					m.Id == typeToken || NodeTitle(m.Type) == typeToken || m.Type.Name == typeToken);
				if (meta == null)
				{
					outError = $"no node type '{typeToken}' (see warudo graph node types)";
					return false;
				}

				Node node = graph.AddNode(meta.Id);
				outLines.Add($"{node.Id} \"{NodeTitle(node)}\"");
				return true;
			}

			if (request.Args.Count < 5 || !TryFindNode(graph, request.Args[4], out Node target, out outError))
			{
				outError ??= $"usage: warudo graph node {sub} <graph> <node> ...";
				return false;
			}

			return HandleEntityVerb(target, sub, request, 5, outLines, out outError);
		}

		// -- Entity ports, shared by assets, plugins and nodes -----

		// argIndex is where the verb's own arguments start
		private static bool HandleEntityVerb(Entity entity, string verb, CommandRequest request, int argIndex,
			List<string> outLines, out string outError)
		{
			outError = null;
			switch (verb)
			{
				case "ports":
				{
					foreach (string key in entity.DataInputPortCollection.GetPorts().Keys)
					{
						object value = entity.DataInputPortCollection.GetPortValue(key);
						outLines.Add($"data {key} = {Reflect.Format(value, true)}");
					}
					foreach (string key in entity.TriggerPortCollection.GetPorts().Keys)
					{
						outLines.Add($"trigger {key}");
					}
					return true;
				}

				case "get":
				{
					if (request.Args.Count <= argIndex)
					{
						outError = "usage: ... get <port.path>";
						return false;
					}
					object value = entity.DataInputPortCollection.GetValueAtPath(request.Args[argIndex]);
					outLines.Add(Reflect.Format(value));
					return true;
				}

				case "set":
				{
					if (request.Args.Count <= argIndex + 1)
					{
						outError = "usage: ... set <port.path> <value...>";
						return false;
					}

					string path = request.Args[argIndex];
					if (!TryResolvePathType(entity, path, out Type valueType, out outError) ||
						!Reflect.TryParseValue(valueType, request.RawRest(argIndex + 1), out object value, out outError))
					{
						return false;
					}

					if (!entity.DataInputPortCollection.SetValueAtPath(path, value, true))
					{
						outError = $"Warudo refused the value for {path}";
						return false;
					}
					outLines.Add(Reflect.Format(entity.DataInputPortCollection.GetValueAtPath(path)));
					return true;
				}

				case "setjson":
				{
					if (request.Args.Count <= argIndex + 1)
					{
						outError = "usage: ... setjson <port.path> <json...>";
						return false;
					}

					string path = request.Args[argIndex];
					if (!entity.DataInputPortCollection.SetSerializedValueAtPath(path, request.RawRest(argIndex + 1), true))
					{
						outError = $"Warudo refused the serialized value for {path}";
						return false;
					}
					outLines.Add(Reflect.Format(entity.DataInputPortCollection.GetValueAtPath(path)));
					return true;
				}

				case "trigger":
				{
					if (request.Args.Count <= argIndex)
					{
						outError = "usage: ... trigger <trigger>";
						return false;
					}
					entity.InvokeTrigger(request.Args[argIndex]);
					return true;
				}
			}

			outError = $"unknown verb '{verb}' (ports|get|set|setjson|trigger)";
			return false;
		}

		// A data input is a public field named by its port key, and a path walks into its value's
		// fields and properties, so the type to parse into comes from the entity's own members
		private static bool TryResolvePathType(Entity entity, string path, out Type type, out string error)
		{
			type = null;
			error = null;
			Type current = entity.GetType();
			foreach (string segment in path.Split('.'))
			{
				MemberInfo member = Reflect.FindMember(current, segment, false, true);
				switch (member)
				{
					case FieldInfo field:
						current = field.FieldType;
						break;
					case PropertyInfo property:
						current = property.PropertyType;
						break;
					default:
						error = $"no member '{segment}' on {Reflect.TypeName(current)}";
						return false;
				}
			}

			type = current;
			return true;
		}

		private static string TypeTitle(Asset asset) => AssetTitle(asset.GetType());

		private static string NodeTitle(Node node) => NodeTitle(node.GetType());

		// The title the add menus show, from the type's own attribute
		private static string AssetTitle(Type type)
		{
			return type.GetCustomAttribute<Warudo.Core.Attributes.AssetTypeAttribute>()?.Title ?? type.Name;
		}

		private static string NodeTitle(Type type)
		{
			return type.GetCustomAttribute<Warudo.Core.Attributes.NodeTypeAttribute>()?.Title ?? type.Name;
		}
	}
}
