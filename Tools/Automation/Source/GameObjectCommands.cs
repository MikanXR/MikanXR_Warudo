using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WarudoAutomation
{
	// go: the scene hierarchy. GameObjects reply as "#id path active|inactive".
	public static class GameObjectCommands
	{
		public const int DefaultTreeDepth = 3;
		public const int MaxFindResults = 200;

		public static void Register(AutomationServer server)
		{
			server.RegisterNamespace("go", new[]
			{
				"go scenes",
				"go roots [sceneName]",
				"go children <target>",
				"go tree <target> [depth]",
				"go find <nameSubstring> [all]",
				"go components <target>",
				"go active <target> [true|false]",
				"go transform <target>",
			}, Handle);
		}

		private static string Line(GameObject go)
		{
			return $"#{go.GetInstanceID()} {Targets.PathOf(go)} {(go.activeInHierarchy ? "active" : go.activeSelf ? "hidden" : "inactive")}";
		}

		private static bool Handle(CommandRequest request, List<string> outLines, out string outError)
		{
			outError = null;
			List<string> args = request.Args;
			switch (request.Verb)
			{
				case "scenes":
					foreach (Scene scene in Targets.LoadedScenes())
					{
						outLines.Add($"{scene.name} roots={scene.rootCount} path={(string.IsNullOrEmpty(scene.path) ? "-" : scene.path)}");
					}
					return true;

				case "roots":
				{
					string sceneName = args.Count > 1 ? args[1] : null;
					foreach (Scene scene in Targets.LoadedScenes())
					{
						if (sceneName != null && scene.name != sceneName)
						{
							continue;
						}
						foreach (GameObject root in scene.GetRootGameObjects())
						{
							if (root != AutomationServer.Instance.gameObject)
							{
								outLines.Add(Line(root));
							}
						}
					}
					return true;
				}

				case "children":
				{
					if (args.Count < 2)
					{
						outError = "usage: go children <target>";
						return false;
					}
					if (!Targets.TryResolveGameObject(args[1], out GameObject go, out outError))
					{
						return false;
					}
					foreach (Transform child in go.transform)
					{
						outLines.Add(Line(child.gameObject));
					}
					return true;
				}

				case "tree":
				{
					if (args.Count < 2)
					{
						outError = "usage: go tree <target> [depth]";
						return false;
					}
					int depth = DefaultTreeDepth;
					if (args.Count > 2 && !int.TryParse(args[2], out depth))
					{
						outError = $"bad depth '{args[2]}'";
						return false;
					}
					if (!Targets.TryResolveGameObject(args[1], out GameObject go, out outError))
					{
						return false;
					}
					AddTree(go.transform, 0, depth, outLines);
					return true;
				}

				case "find":
				{
					if (args.Count < 2)
					{
						outError = "usage: go find <nameSubstring> [all]";
						return false;
					}
					bool includeInactive = args.Count > 2 && args[2] == "all";
					int found = 0;
					foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
					{
						// Prefabs and other assets are not in a scene
						if (go == null || !go.scene.IsValid() || go == AutomationServer.Instance.gameObject)
						{
							continue;
						}
						if (!includeInactive && !go.activeInHierarchy)
						{
							continue;
						}
						if (go.name.IndexOf(args[1], StringComparison.OrdinalIgnoreCase) < 0)
						{
							continue;
						}
						if (found++ == MaxFindResults)
						{
							outLines.Add("...");
							break;
						}
						outLines.Add(Line(go));
					}
					return true;
				}

				case "components":
				{
					if (args.Count < 2)
					{
						outError = "usage: go components <target>";
						return false;
					}
					if (!Targets.TryResolveGameObject(args[1], out GameObject go, out outError))
					{
						return false;
					}
					foreach (Component component in go.GetComponents<Component>())
					{
						if (component == null)
						{
							outLines.Add("missing (script failed to load)");
							continue;
						}
						string state = component is Behaviour behaviour ? (behaviour.enabled ? "enabled" : "disabled") : "-";
						outLines.Add($"#{component.GetInstanceID()} {component.GetType().FullName} {state}");
					}
					return true;
				}

				case "active":
				{
					if (args.Count < 2)
					{
						outError = "usage: go active <target> [true|false]";
						return false;
					}
					if (!Targets.TryResolveGameObject(args[1], out GameObject go, out outError))
					{
						return false;
					}
					if (args.Count > 2)
					{
						if (!Reflect.TryParseValue(typeof(bool), args[2], out object active, out outError))
						{
							return false;
						}
						go.SetActive((bool)active);
					}
					outLines.Add($"self {(go.activeSelf ? "true" : "false")}");
					outLines.Add($"hierarchy {(go.activeInHierarchy ? "true" : "false")}");
					return true;
				}

				case "transform":
				{
					if (args.Count < 2)
					{
						outError = "usage: go transform <target>";
						return false;
					}
					if (!Targets.TryResolveGameObject(args[1], out GameObject go, out outError))
					{
						return false;
					}
					Transform t = go.transform;
					outLines.Add($"transform #{t.GetInstanceID()}");
					outLines.Add($"localPosition {Reflect.Format(t.localPosition)}");
					outLines.Add($"localRotation {Reflect.Format(t.localRotation)}");
					outLines.Add($"localEuler {Reflect.Format(t.localEulerAngles)}");
					outLines.Add($"localScale {Reflect.Format(t.localScale)}");
					outLines.Add($"position {Reflect.Format(t.position)}");
					outLines.Add($"rotation {Reflect.Format(t.rotation)}");
					outLines.Add($"lossyScale {Reflect.Format(t.lossyScale)}");
					outLines.Add($"parent {(t.parent != null ? Targets.Describe(t.parent.gameObject) : "none")}");
					return true;
				}
			}

			outError = "usage: go scenes|roots|children|tree|find|components|active|transform";
			return false;
		}

		private static void AddTree(Transform transform, int level, int maxDepth, List<string> outLines)
		{
			GameObject go = transform.gameObject;
			outLines.Add($"{new string(' ', level * 2)}#{go.GetInstanceID()} {go.name}{(go.activeSelf ? string.Empty : " (inactive)")}");
			if (level >= maxDepth)
			{
				if (transform.childCount > 0)
				{
					outLines.Add($"{new string(' ', (level + 1) * 2)}... {transform.childCount} children");
				}
				return;
			}
			foreach (Transform child in transform)
			{
				AddTree(child, level + 1, maxDepth, outLines);
			}
		}
	}
}
