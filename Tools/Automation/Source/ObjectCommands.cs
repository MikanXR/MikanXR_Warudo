using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WarudoAutomation
{
	// obj, static and type: reflection over live objects, over a type's statics, and over the
	// loaded types themselves
	public static class ObjectCommands
	{
		public const int MaxTypeResults = 200;

		public static void Register(AutomationServer server)
		{
			server.RegisterNamespace("obj", new[]
			{
				"obj get <target> [member.path]",
				"obj set <target> <member.path> <value...>",
				"obj call <target> <method> [args...]",
				"obj inspect <target> [member.path]",
				"obj members <target> [filter]",
			}, HandleObject);

			server.RegisterNamespace("static", new[]
			{
				"static get <Type> <member.path>",
				"static set <Type> <member.path> <value...>",
				"static call <Type> <method> [args...]",
				"static inspect <Type>",
			}, HandleStatic);

			server.RegisterNamespace("type", new[]
			{
				"type find <nameSubstring> [assemblySubstring]",
				"type members <Type> [filter]",
				"type instances <Type>",
			}, HandleType);
		}

		private static bool HandleObject(CommandRequest request, List<string> outLines, out string outError)
		{
			outError = null;
			List<string> args = request.Args;
			if (args.Count < 2)
			{
				outError = "usage: obj get|set|call|inspect|members <target> ...";
				return false;
			}

			if (!Targets.TryResolve(args[1], out object target, out outError))
			{
				return false;
			}

			string path = args.Count > 2 ? args[2] : string.Empty;
			switch (request.Verb)
			{
				case "get":
				{
					if (!Reflect.TryGetPath(target, null, path, out object value, out outError))
					{
						return false;
					}
					outLines.Add(Reflect.Format(value));
					return true;
				}

				case "set":
				{
					if (args.Count < 4)
					{
						outError = "usage: obj set <target> <member.path> <value...>";
						return false;
					}
					return Reflect.TrySetPath(target, null, path, string.Join(" ", args.Skip(3)), out outError);
				}

				case "call":
				{
					if (args.Count < 3)
					{
						outError = "usage: obj call <target> <method> [args...]";
						return false;
					}
					return Call(target, target.GetType(), args[2], args.Skip(3).ToList(), outLines, out outError);
				}

				case "inspect":
				{
					if (!Reflect.TryGetPath(target, null, path, out object value, out outError))
					{
						return false;
					}
					if (value == null)
					{
						outLines.Add("null");
						return true;
					}
					outLines.Add($"type {value.GetType().FullName}");
					if (value is Object unityObject)
					{
						outLines.Add($"object {Reflect.Format(unityObject)}");
					}
					outLines.AddRange(Reflect.Inspect(value, value.GetType()));
					return true;
				}

				case "members":
				{
					string filter = args.Count > 2 ? args[2] : null;
					outLines.AddRange(Reflect.ListMembers(target.GetType(), Reflect.AnyInstance | Reflect.AnyStatic, filter));
					return true;
				}
			}

			outError = "usage: obj get|set|call|inspect|members <target> ...";
			return false;
		}

		private static bool HandleStatic(CommandRequest request, List<string> outLines, out string outError)
		{
			outError = null;
			List<string> args = request.Args;
			if (args.Count < 2)
			{
				outError = "usage: static get|set|call|inspect <Type> ...";
				return false;
			}

			if (!TypeIndex.TryResolve(args[1], out Type type, out outError))
			{
				return false;
			}

			switch (request.Verb)
			{
				case "get":
				{
					if (args.Count < 3)
					{
						outError = "usage: static get <Type> <member.path>";
						return false;
					}
					if (!Reflect.TryGetPath(null, type, args[2], out object value, out outError))
					{
						return false;
					}
					outLines.Add(Reflect.Format(value));
					return true;
				}

				case "set":
				{
					if (args.Count < 4)
					{
						outError = "usage: static set <Type> <member.path> <value...>";
						return false;
					}
					return Reflect.TrySetPath(null, type, args[2], string.Join(" ", args.Skip(3)), out outError);
				}

				case "call":
				{
					if (args.Count < 3)
					{
						outError = "usage: static call <Type> <method> [args...]";
						return false;
					}
					return Call(null, type, args[2], args.Skip(3).ToList(), outLines, out outError);
				}

				case "inspect":
					outLines.Add($"type {type.FullName}");
					outLines.AddRange(Reflect.Inspect(null, type));
					return true;
			}

			outError = "usage: static get|set|call|inspect <Type> ...";
			return false;
		}

		private static bool Call(object target, Type type, string methodName, List<string> callArgs, List<string> outLines, out string outError)
		{
			if (!Reflect.TryCall(target, type, methodName, callArgs, out object result, out bool returnsVoid, out outError))
			{
				return false;
			}

			// A coroutine method only builds its iterator; run it the way Unity would
			if (result is IEnumerator routine && target is MonoBehaviour behaviour)
			{
				behaviour.StartCoroutine(routine);
				outLines.Add("coroutine started");
				return true;
			}

			if (!returnsVoid)
			{
				outLines.Add(Reflect.Format(result));
			}
			return true;
		}

		private static bool HandleType(CommandRequest request, List<string> outLines, out string outError)
		{
			outError = null;
			List<string> args = request.Args;
			switch (request.Verb)
			{
				case "find":
				{
					if (args.Count < 2)
					{
						outError = "usage: type find <nameSubstring> [assemblySubstring]";
						return false;
					}
					string assemblyFilter = args.Count > 2 ? args[2] : null;
					int found = 0;
					foreach (Type type in TypeIndex.AllTypes)
					{
						if (type.FullName == null || type.FullName.IndexOf(args[1], StringComparison.OrdinalIgnoreCase) < 0)
						{
							continue;
						}
						string assemblyName = type.Assembly.GetName().Name;
						if (assemblyFilter != null && assemblyName.IndexOf(assemblyFilter, StringComparison.OrdinalIgnoreCase) < 0)
						{
							continue;
						}
						if (found++ == MaxTypeResults)
						{
							outLines.Add("...");
							break;
						}
						outLines.Add($"{type.FullName} {assemblyName} {Kind(type)}");
					}
					return true;
				}

				case "members":
				{
					if (args.Count < 2)
					{
						outError = "usage: type members <Type> [filter]";
						return false;
					}
					if (!TypeIndex.TryResolve(args[1], out Type type, out outError))
					{
						return false;
					}
					string filter = args.Count > 2 ? args[2] : null;
					outLines.Add($"type {type.FullName} base {(type.BaseType != null ? type.BaseType.FullName : "none")}");
					outLines.AddRange(Reflect.ListMembers(type, Reflect.AnyInstance | Reflect.AnyStatic, filter));
					return true;
				}

				case "instances":
				{
					if (args.Count < 2)
					{
						outError = "usage: type instances <Type>";
						return false;
					}
					if (!TypeIndex.TryResolve(args[1], out Type type, out outError))
					{
						return false;
					}
					if (!typeof(Object).IsAssignableFrom(type))
					{
						outError = $"{type.FullName} is not a Unity object type, so its instances are not tracked";
						return false;
					}
					foreach (Object instance in Object.FindObjectsOfType(type, true))
					{
						outLines.Add(Targets.Describe(instance));
					}
					return true;
				}
			}

			outError = "usage: type find|members|instances";
			return false;
		}

		private static string Kind(Type type)
		{
			if (type.IsEnum)
			{
				return "enum";
			}
			if (type.IsInterface)
			{
				return "interface";
			}
			if (type.IsValueType)
			{
				return "struct";
			}
			if (typeof(MonoBehaviour).IsAssignableFrom(type))
			{
				return "monobehaviour";
			}
			if (typeof(ScriptableObject).IsAssignableFrom(type))
			{
				return "scriptableobject";
			}
			if (type.IsAbstract && type.IsSealed)
			{
				return "static";
			}
			return "class";
		}
	}
}
