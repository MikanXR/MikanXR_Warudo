using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WarudoAutomation
{
	// Resolves the target token the go, obj, ui and screenshot commands take:
	//   #<instanceId>           any live Unity object by instance id
	//   /Root/Child             a GameObject by hierarchy path from a scene root
	//   @<TypeName>             the first live object of a Unity type (singleton managers)
	//   $<asset>                a Warudo asset by id, name or type title: its GameObject when it
	//                           has one, else the asset itself
	// followed optionally by :<ComponentType>[index] to pick a component on the GameObject the
	// rest names (/Canvas/Panel:Button, #1234:Animator, $MikanXR:MikanClient).
	public static class Targets
	{
		private static MethodInfo s_findFromInstanceId;
		private static bool s_lookedUpFindFromInstanceId;

		public static bool TryResolve(string token, out object target, out string error)
		{
			target = null;
			error = null;

			string objectPart = token;
			string componentPart = null;
			int colon = token.LastIndexOf(':');
			if (colon > 0 && colon < token.Length - 1)
			{
				objectPart = token.Substring(0, colon);
				componentPart = token.Substring(colon + 1);
			}

			if (!TryResolveObject(objectPart, out object resolved, out error))
			{
				// A GameObject whose name holds a colon resolves when read as a whole path
				if (componentPart != null && TryResolveObject(token, out resolved, out _))
				{
					target = resolved;
					error = null;
					return true;
				}
				return false;
			}

			if (componentPart == null)
			{
				target = resolved;
				return true;
			}

			GameObject go = AsGameObject(resolved);
			if (go == null)
			{
				error = $"'{objectPart}' is not a GameObject, so it has no component '{componentPart}'";
				return false;
			}

			return TryGetComponent(go, componentPart, out target, out error);
		}

		public static bool TryResolveGameObject(string token, out GameObject go, out string error)
		{
			go = null;
			if (!TryResolve(token, out object target, out error))
			{
				return false;
			}

			go = AsGameObject(target);
			if (go == null)
			{
				error = $"'{token}' is not a GameObject or component";
				return false;
			}
			return true;
		}

		public static GameObject AsGameObject(object target)
		{
			if (target is GameObject go && go != null)
			{
				return go;
			}
			if (target is Component component && component != null)
			{
				return component.gameObject;
			}
			return null;
		}

		private static bool TryResolveObject(string token, out object target, out string error)
		{
			target = null;
			error = null;

			if (token.StartsWith("$") && token.Length > 1)
			{
				if (!WarudoCommands.TryFindAsset(token.Substring(1), out Warudo.Core.Scenes.Asset asset, out error))
				{
					return false;
				}

				GameObject assetObject = (asset as Warudo.Plugins.Core.Assets.GameObjectAsset)?.GameObject;
				target = assetObject != null ? (object)assetObject : asset;
				return true;
			}

			if (token.StartsWith("#"))
			{
				if (!int.TryParse(token.Substring(1), out int instanceId))
				{
					error = $"bad instance id '{token}'";
					return false;
				}
				Object found = FindByInstanceId(instanceId);
				if (found == null)
				{
					error = $"no live object with instance id {instanceId}";
					return false;
				}
				target = found;
				return true;
			}

			if (token.StartsWith("@"))
			{
				if (!TypeIndex.TryResolve(token.Substring(1), out Type type, out error))
				{
					return false;
				}
				if (!typeof(Object).IsAssignableFrom(type))
				{
					error = $"{type.FullName} is not a Unity object type; use the static namespace for plain types";
					return false;
				}
				Object[] found = Object.FindObjectsOfType(type, true);
				if (found.Length == 0)
				{
					error = $"no live {type.FullName}";
					return false;
				}
				target = found[0];
				return true;
			}

			if (token.StartsWith("/"))
			{
				GameObject go = FindByPath(token);
				if (go == null)
				{
					error = $"no GameObject at '{token}'";
					return false;
				}
				target = go;
				return true;
			}

			error = $"bad target '{token}' (#id, /path, @Type, or $asset, with an optional :Component)";
			return false;
		}

		private static bool TryGetComponent(GameObject go, string componentSpec, out object target, out string error)
		{
			target = null;
			int index = 0;
			string typeName = componentSpec;
			int bracket = componentSpec.IndexOf('[');
			if (bracket > 0 && componentSpec.EndsWith("]"))
			{
				typeName = componentSpec.Substring(0, bracket);
				if (!int.TryParse(componentSpec.Substring(bracket + 1, componentSpec.Length - bracket - 2), out index))
				{
					error = $"bad component index in '{componentSpec}'";
					return false;
				}
			}

			// Match the component's own type names first, so a type the type index finds ambiguous
			// still resolves when only one of them is on the object
			var matches = new List<Component>();
			foreach (Component component in go.GetComponents<Component>())
			{
				if (component == null)
				{
					continue;
				}
				for (Type t = component.GetType(); t != null && t != typeof(Component); t = t.BaseType)
				{
					if (t.Name == typeName || t.FullName == typeName)
					{
						matches.Add(component);
						break;
					}
				}
			}

			if (matches.Count == 0)
			{
				error = $"{PathOf(go)} has no component '{typeName}'";
				return false;
			}
			if (index < 0 || index >= matches.Count)
			{
				error = $"{PathOf(go)} has {matches.Count} '{typeName}' components";
				return false;
			}

			target = matches[index];
			error = null;
			return true;
		}

		public static Object FindByInstanceId(int instanceId)
		{
			if (!s_lookedUpFindFromInstanceId)
			{
				s_lookedUpFindFromInstanceId = true;
				s_findFromInstanceId = typeof(Object).GetMethod("FindObjectFromInstanceID",
					BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(int) }, null);
			}

			if (s_findFromInstanceId != null)
			{
				try
				{
					var found = s_findFromInstanceId.Invoke(null, new object[] { instanceId }) as Object;
					if (found != null)
					{
						return found;
					}
				}
				catch (Exception)
				{
				}
			}

			foreach (Object candidate in Resources.FindObjectsOfTypeAll<Object>())
			{
				if (candidate != null && candidate.GetInstanceID() == instanceId)
				{
					return candidate;
				}
			}
			return null;
		}

		// Every loaded scene's roots plus the DontDestroyOnLoad scene's, where Warudo and its
		// plugins keep their long-lived objects
		public static List<Scene> LoadedScenes()
		{
			var scenes = new List<Scene>();
			for (int i = 0; i < SceneManager.sceneCount; ++i)
			{
				Scene scene = SceneManager.GetSceneAt(i);
				if (scene.isLoaded)
				{
					scenes.Add(scene);
				}
			}

			if (AutomationServer.Instance != null)
			{
				Scene dontDestroy = AutomationServer.Instance.gameObject.scene;
				if (dontDestroy.IsValid() && !scenes.Contains(dontDestroy))
				{
					scenes.Add(dontDestroy);
				}
			}
			return scenes;
		}

		public static IEnumerable<GameObject> AllRoots()
		{
			foreach (Scene scene in LoadedScenes())
			{
				foreach (GameObject root in scene.GetRootGameObjects())
				{
					if (root != null && root != AutomationServer.Instance?.gameObject)
					{
						yield return root;
					}
				}
			}
		}

		public static GameObject FindByPath(string path)
		{
			string[] names = path.Trim('/').Split('/');
			if (names.Length == 0 || names[0].Length == 0)
			{
				return null;
			}

			foreach (GameObject root in AllRoots())
			{
				if (root.name != names[0])
				{
					continue;
				}

				Transform current = root.transform;
				for (int i = 1; i < names.Length && current != null; ++i)
				{
					current = FindChild(current, names[i]);
				}

				if (current != null)
				{
					return current.gameObject;
				}
			}
			return null;
		}

		private static Transform FindChild(Transform parent, string name)
		{
			for (int i = 0; i < parent.childCount; ++i)
			{
				Transform child = parent.GetChild(i);
				if (child.name == name)
				{
					return child;
				}
			}
			return null;
		}

		public static string PathOf(GameObject go)
		{
			if (go == null)
			{
				return "null";
			}

			var builder = new StringBuilder();
			for (Transform t = go.transform; t != null; t = t.parent)
			{
				builder.Insert(0, "/" + t.name);
			}
			return builder.ToString();
		}

		// "#id Type /path" for scene objects, "#id Type "name"" for assets
		public static string Describe(Object obj)
		{
			if (obj == null)
			{
				return "null";
			}

			string typeName = obj.GetType().Name;
			if (obj is GameObject go)
			{
				return $"#{go.GetInstanceID()} GameObject {PathOf(go)}";
			}
			if (obj is Component component)
			{
				return $"#{component.GetInstanceID()} {typeName} {PathOf(component.gameObject)}";
			}
			return $"#{obj.GetInstanceID()} {typeName} \"{obj.name}\"";
		}
	}
}
