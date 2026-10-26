using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WarudoAutomation
{
	// Reflection over live objects and types: dotted member paths with [index] steps, text values
	// parsed to and formatted from a member's type, member listings, and method calls. Private
	// members are reachable, since Warudo's state mostly lives in them.
	public static class Reflect
	{
		public const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		public const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
		public const int MaxListedElements = 20;

		private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

		// Properties whose getter creates an asset or otherwise mutates the object; reading them
		// from an inspection would leak instances, so only an explicit get reaches them
		private static readonly HashSet<string> s_unsafeInspectProperties = new HashSet<string>
		{
			"material",
			"materials",
			"mesh",
		};

		private struct Segment
		{
			public string Name;
			public string Index;
		}

		// ---- Member paths ------------------------------------------------------------------

		private static bool TryParsePath(string path, List<Segment> segments, out string error)
		{
			error = null;
			segments.Clear();
			if (string.IsNullOrEmpty(path))
			{
				return true;
			}

			int i = 0;
			while (i < path.Length)
			{
				char c = path[i];
				if (c == '.')
				{
					++i;
					continue;
				}
				if (c == '[')
				{
					int close = path.IndexOf(']', i);
					if (close < 0)
					{
						error = $"unterminated [ in '{path}'";
						return false;
					}
					segments.Add(new Segment { Index = path.Substring(i + 1, close - i - 1).Trim('"', '\'') });
					i = close + 1;
					continue;
				}

				int end = i;
				while (end < path.Length && path[end] != '.' && path[end] != '[')
				{
					++end;
				}
				segments.Add(new Segment { Name = path.Substring(i, end - i) });
				i = end;
			}
			return true;
		}

		// Walks the path from root. A null root with a rootType walks from the type's statics.
		public static bool TryGetPath(object root, Type rootType, string path, out object value, out string error)
		{
			value = null;
			var segments = new List<Segment>();
			if (!TryParsePath(path, segments, out error))
			{
				return false;
			}

			if (segments.Count == 0)
			{
				value = root;
				return true;
			}

			object current = root;
			Type currentType = rootType ?? root?.GetType();
			foreach (Segment segment in segments)
			{
				if (!TryGetSegment(current, currentType, segment, out current, out Type memberType, out error))
				{
					return false;
				}
				currentType = current?.GetType() ?? memberType;
			}

			value = current;
			return true;
		}

		public static bool TrySetPath(object root, Type rootType, string path, string valueText, out string error)
		{
			var segments = new List<Segment>();
			if (!TryParsePath(path, segments, out error))
			{
				return false;
			}
			if (segments.Count == 0)
			{
				error = "a set needs a member path";
				return false;
			}

			// Read the holders along the path, so a struct holder modified at the end is written
			// back into its own holder in turn (transform.position.x writes position back)
			var holders = new List<object> { root };
			var holderTypes = new List<Type> { rootType ?? root?.GetType() };
			for (int i = 0; i < segments.Count - 1; ++i)
			{
				if (!TryGetSegment(holders[i], holderTypes[i], segments[i], out object next, out Type nextType, out error))
				{
					return false;
				}
				if (next == null)
				{
					error = $"'{SegmentText(segments[i])}' is null";
					return false;
				}
				holders.Add(next);
				holderTypes.Add(next.GetType());
			}

			int last = segments.Count - 1;
			if (!TryGetSegmentType(holders[last], holderTypes[last], segments[last], out Type targetType, out error))
			{
				return false;
			}
			if (!TryParseValue(targetType, valueText, out object parsed, out error))
			{
				return false;
			}
			if (!TrySetSegment(holders[last], holderTypes[last], segments[last], parsed, out error))
			{
				return false;
			}

			for (int i = last - 1; i >= 0; --i)
			{
				if (!holderTypes[i + 1].IsValueType)
				{
					break;
				}
				if (!TrySetSegment(holders[i], holderTypes[i], segments[i], holders[i + 1], out error))
				{
					return false;
				}
			}
			return true;
		}

		private static string SegmentText(Segment segment)
		{
			return segment.Name ?? $"[{segment.Index}]";
		}

		public static MemberInfo FindMember(Type type, string name, bool wantStatic, bool wantInstance)
		{
			BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
			if (wantStatic)
			{
				flags |= BindingFlags.Static;
			}
			if (wantInstance)
			{
				flags |= BindingFlags.Instance;
			}

			for (Type t = type; t != null; t = t.BaseType)
			{
				FieldInfo field = t.GetField(name, flags);
				if (field != null)
				{
					return field;
				}

				PropertyInfo property = t.GetProperties(flags).FirstOrDefault(p => p.Name == name && p.GetIndexParameters().Length == 0);
				if (property != null)
				{
					return property;
				}
			}

			return null;
		}

		private static bool TryGetSegment(object holder, Type holderType, Segment segment, out object value, out Type valueType, out string error)
		{
			value = null;
			valueType = typeof(object);
			error = null;

			if (segment.Name != null)
			{
				MemberInfo member = FindMember(holderType, segment.Name, true, holder != null);
				if (member == null)
				{
					error = $"{holderType.Name} has no {(holder == null ? "static " : string.Empty)}field or property '{segment.Name}'";
					return false;
				}

				if (member is FieldInfo field)
				{
					valueType = field.FieldType;
					value = field.GetValue(field.IsStatic ? null : holder);
				}
				else
				{
					var property = (PropertyInfo)member;
					valueType = property.PropertyType;
					if (property.GetGetMethod(true) == null)
					{
						error = $"{holderType.Name}.{segment.Name} has no getter";
						return false;
					}
					value = property.GetValue(property.GetGetMethod(true).IsStatic ? null : holder);
				}
				return true;
			}

			if (holder == null)
			{
				error = $"cannot index [{segment.Index}] into null";
				return false;
			}

			if (holder is IDictionary dictionary)
			{
				Type keyType = DictionaryTypes(holder.GetType()).Key;
				if (!TryParseValue(keyType, segment.Index, out object key, out error))
				{
					return false;
				}
				if (!dictionary.Contains(key))
				{
					error = $"no key '{segment.Index}'";
					return false;
				}
				value = dictionary[key];
				valueType = DictionaryTypes(holder.GetType()).Value;
				return true;
			}

			if (!int.TryParse(segment.Index, out int index))
			{
				error = $"[{segment.Index}] is not an integer index into {holder.GetType().Name}";
				return false;
			}

			if (holder is IList list)
			{
				if (index < 0 || index >= list.Count)
				{
					error = $"index {index} out of range (count {list.Count})";
					return false;
				}
				value = list[index];
				valueType = ElementType(holder.GetType());
				return true;
			}

			if (holder is IEnumerable enumerable && !(holder is string))
			{
				int position = 0;
				foreach (object element in enumerable)
				{
					if (position++ == index)
					{
						value = element;
						valueType = ElementType(holder.GetType());
						return true;
					}
				}
				error = $"index {index} out of range (count {position})";
				return false;
			}

			error = $"{holder.GetType().Name} cannot be indexed";
			return false;
		}

		private static bool TryGetSegmentType(object holder, Type holderType, Segment segment, out Type type, out string error)
		{
			type = typeof(object);
			error = null;
			if (segment.Name != null)
			{
				MemberInfo member = FindMember(holderType, segment.Name, true, holder != null);
				if (member == null)
				{
					error = $"{holderType.Name} has no field or property '{segment.Name}'";
					return false;
				}
				type = member is FieldInfo field ? field.FieldType : ((PropertyInfo)member).PropertyType;
				return true;
			}

			type = holder is IDictionary ? DictionaryTypes(holderType).Value : ElementType(holderType);
			return true;
		}

		private static bool TrySetSegment(object holder, Type holderType, Segment segment, object value, out string error)
		{
			error = null;
			if (segment.Name != null)
			{
				MemberInfo member = FindMember(holderType, segment.Name, true, holder != null);
				if (member is FieldInfo field)
				{
					if (field.IsLiteral)
					{
						error = $"{holderType.Name}.{segment.Name} is a constant";
						return false;
					}
					field.SetValue(field.IsStatic ? null : holder, value);
					return true;
				}

				var property = member as PropertyInfo;
				MethodInfo setter = property?.GetSetMethod(true);
				if (setter == null)
				{
					error = $"{holderType.Name}.{segment.Name} is read-only";
					return false;
				}
				property.SetValue(setter.IsStatic ? null : holder, value);
				return true;
			}

			if (holder is IDictionary dictionary)
			{
				if (!TryParseValue(DictionaryTypes(holderType).Key, segment.Index, out object key, out error))
				{
					return false;
				}
				dictionary[key] = value;
				return true;
			}

			if (holder is IList list && int.TryParse(segment.Index, out int index))
			{
				if (index < 0 || index >= list.Count)
				{
					error = $"index {index} out of range (count {list.Count})";
					return false;
				}
				list[index] = value;
				return true;
			}

			error = $"cannot assign [{segment.Index}] on {holderType.Name}";
			return false;
		}

		private static Type ElementType(Type collectionType)
		{
			if (collectionType.IsArray)
			{
				return collectionType.GetElementType();
			}
			foreach (Type iface in collectionType.GetInterfaces().Concat(new[] { collectionType }))
			{
				if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
				{
					return iface.GetGenericArguments()[0];
				}
			}
			return typeof(object);
		}

		private static (Type Key, Type Value) DictionaryTypes(Type dictionaryType)
		{
			foreach (Type iface in dictionaryType.GetInterfaces().Concat(new[] { dictionaryType }))
			{
				if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IDictionary<,>))
				{
					Type[] args = iface.GetGenericArguments();
					return (args[0], args[1]);
				}
			}
			return (typeof(string), typeof(object));
		}

		// ---- Values ------------------------------------------------------------------------

		private static float[] ParseFloats(string text, int minCount, int maxCount, string typeName)
		{
			string[] parts = text.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length < minCount || parts.Length > maxCount)
			{
				throw new FormatException(minCount == maxCount
					? $"{typeName} takes {minCount} numbers"
					: $"{typeName} takes {minCount} to {maxCount} numbers");
			}
			return parts.Select(p => float.Parse(p, NumberStyles.Float, Invariant)).ToArray();
		}

		public static bool TryParseValue(Type type, string text, out object value, out string error)
		{
			value = null;
			error = null;
			text = text ?? string.Empty;

			try
			{
				Type underlying = Nullable.GetUnderlyingType(type);
				if (underlying != null)
				{
					if (text == "null")
					{
						return true;
					}
					type = underlying;
				}

				if (type == typeof(string) || type == typeof(object))
				{
					value = text == "null" ? null : text;
					return true;
				}
				if (type == typeof(bool))
				{
					switch (text.ToLowerInvariant())
					{
						case "true":
						case "1":
						case "on":
							value = true;
							return true;
						case "false":
						case "0":
						case "off":
							value = false;
							return true;
					}
					error = $"'{text}' is not a bool (true|false)";
					return false;
				}
				if (type.IsEnum)
				{
					if (long.TryParse(text, out long number))
					{
						value = Enum.ToObject(type, number);
					}
					else
					{
						value = Enum.Parse(type, text.Replace('|', ','), true);
					}
					return true;
				}
				if (type.IsPrimitive || type == typeof(decimal))
				{
					value = Convert.ChangeType(text, type, Invariant);
					return true;
				}

				if (type == typeof(Vector2))
				{
					float[] f = ParseFloats(text, 2, 2, "Vector2");
					value = new Vector2(f[0], f[1]);
					return true;
				}
				if (type == typeof(Vector3))
				{
					float[] f = ParseFloats(text, 3, 3, "Vector3");
					value = new Vector3(f[0], f[1], f[2]);
					return true;
				}
				if (type == typeof(Vector4))
				{
					float[] f = ParseFloats(text, 4, 4, "Vector4");
					value = new Vector4(f[0], f[1], f[2], f[3]);
					return true;
				}
				if (type == typeof(Vector2Int))
				{
					float[] f = ParseFloats(text, 2, 2, "Vector2Int");
					value = new Vector2Int((int)f[0], (int)f[1]);
					return true;
				}
				if (type == typeof(Vector3Int))
				{
					float[] f = ParseFloats(text, 3, 3, "Vector3Int");
					value = new Vector3Int((int)f[0], (int)f[1], (int)f[2]);
					return true;
				}
				if (type == typeof(Quaternion))
				{
					// Four numbers are x y z w; three are Euler angles in degrees
					float[] f = ParseFloats(text, 3, 4, "Quaternion");
					value = f.Length == 4 ? new Quaternion(f[0], f[1], f[2], f[3]) : Quaternion.Euler(f[0], f[1], f[2]);
					return true;
				}
				if (type == typeof(Color))
				{
					float[] f = ParseFloats(text, 3, 4, "Color");
					value = new Color(f[0], f[1], f[2], f.Length == 4 ? f[3] : 1f);
					return true;
				}
				if (type == typeof(Color32))
				{
					float[] f = ParseFloats(text, 3, 4, "Color32");
					value = new Color32((byte)f[0], (byte)f[1], (byte)f[2], f.Length == 4 ? (byte)f[3] : (byte)255);
					return true;
				}
				if (type == typeof(Rect))
				{
					float[] f = ParseFloats(text, 4, 4, "Rect");
					value = new Rect(f[0], f[1], f[2], f[3]);
					return true;
				}

				if (typeof(Object).IsAssignableFrom(type))
				{
					if (text == "null")
					{
						return true;
					}
					if (!Targets.TryResolve(text, out object target, out error))
					{
						return false;
					}
					if (typeof(Component).IsAssignableFrom(type) && target is GameObject go)
					{
						target = go.GetComponent(type);
					}
					if (target == null || !type.IsInstanceOfType(target))
					{
						error = $"'{text}' is not a {type.Name}";
						return false;
					}
					value = target;
					return true;
				}

				if (type == typeof(Type))
				{
					if (!TypeIndex.TryResolve(text, out Type resolved, out error))
					{
						return false;
					}
					value = resolved;
					return true;
				}

				MethodInfo parse = type.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
				if (parse != null)
				{
					value = parse.Invoke(null, new object[] { text });
					return true;
				}

				if (!type.IsValueType && text == "null")
				{
					return true;
				}

				error = $"cannot parse a {type.Name} from text";
				return false;
			}
			catch (Exception e)
			{
				error = $"'{text}' is not a {type.Name}: {AutomationServer.DescribeException(e)}";
				return false;
			}
		}

		private static string F(float value)
		{
			return value.ToString("R", Invariant);
		}

		// One value as text. Inline values have their newlines escaped so they fit on one line.
		public static string Format(object value, bool inline = false, int depth = 0)
		{
			if (value == null)
			{
				return "null";
			}

			if (value is Object unityObject)
			{
				// A destroyed Unity object is non-null in C# but compares equal to null
				return unityObject == null ? "null (destroyed)" : Targets.Describe(unityObject);
			}

			switch (value)
			{
				case string s:
					return inline ? "\"" + s.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"") + "\"" : s;
				case bool b:
					return b ? "true" : "false";
				case float f:
					return F(f);
				case double d:
					return d.ToString("R", Invariant);
				case Vector2 v:
					return $"{F(v.x)} {F(v.y)}";
				case Vector3 v:
					return $"{F(v.x)} {F(v.y)} {F(v.z)}";
				case Vector4 v:
					return $"{F(v.x)} {F(v.y)} {F(v.z)} {F(v.w)}";
				case Vector2Int v:
					return $"{v.x} {v.y}";
				case Vector3Int v:
					return $"{v.x} {v.y} {v.z}";
				case Quaternion q:
					return $"{F(q.x)} {F(q.y)} {F(q.z)} {F(q.w)}";
				case Color c:
					return $"{F(c.r)} {F(c.g)} {F(c.b)} {F(c.a)}";
				case Color32 c:
					return $"{c.r} {c.g} {c.b} {c.a}";
				case Rect r:
					return $"{F(r.x)} {F(r.y)} {F(r.width)} {F(r.height)}";
				case Type t:
					return t.FullName;
				case Enum e:
					return e.ToString();
				case Delegate del:
					return FormatDelegate(del);
			}

			if (value is IFormattable formattable && value.GetType().IsPrimitive)
			{
				return formattable.ToString(null, Invariant);
			}

			if (value is IDictionary dictionary)
			{
				if (depth > 0)
				{
					return $"{{{dictionary.Count} entries}}";
				}
				var parts = new List<string>();
				foreach (DictionaryEntry entry in dictionary)
				{
					if (parts.Count == MaxListedElements)
					{
						parts.Add("...");
						break;
					}
					parts.Add($"{Format(entry.Key, true, depth + 1)}: {Format(entry.Value, true, depth + 1)}");
				}
				return $"{{{dictionary.Count}}} {{{string.Join(", ", parts)}}}";
			}

			if (value is IEnumerable enumerable)
			{
				int count = 0;
				var parts = new List<string>();
				foreach (object element in enumerable)
				{
					if (count < MaxListedElements && depth == 0)
					{
						parts.Add(Format(element, true, depth + 1));
					}
					++count;
				}
				if (depth > 0)
				{
					return $"[{count} items]";
				}
				if (count > MaxListedElements)
				{
					parts.Add("...");
				}
				return $"[{count}] [{string.Join(", ", parts)}]";
			}

			Type type = value.GetType();
			MethodInfo toString = type.GetMethod("ToString", Type.EmptyTypes);
			string text = toString != null && toString.DeclaringType != typeof(object) && toString.DeclaringType != typeof(ValueType)
				? value.ToString()
				: $"{{{type.FullName}}}";
			return inline ? text.Replace("\r", "\\r").Replace("\n", "\\n") : text;
		}

		private static string FormatDelegate(Delegate del)
		{
			var targets = del.GetInvocationList().Select(d => $"{d.Method.DeclaringType?.Name}.{d.Method.Name}");
			return $"delegate [{string.Join(", ", targets)}]";
		}

		// ---- Listings ----------------------------------------------------------------------

		public static string TypeName(Type type)
		{
			if (type == null)
			{
				return "?";
			}
			if (!type.IsGenericType)
			{
				return type.Name;
			}
			string name = type.Name;
			int tick = name.IndexOf('`');
			if (tick > 0)
			{
				name = name.Substring(0, tick);
			}
			return $"{name}<{string.Join(",", type.GetGenericArguments().Select(TypeName))}>";
		}

		private static bool IsUnityEngineType(Type type)
		{
			string assemblyName = type.Assembly.GetName().Name;
			return assemblyName.StartsWith("UnityEngine");
		}

		// Fields of the type and its bases, stopping at Unity's own base classes, whose managed
		// fields are native bookkeeping
		public static IEnumerable<FieldInfo> Fields(Type type, BindingFlags scope)
		{
			for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
			{
				if (IsUnityEngineType(t) && t != type)
				{
					yield break;
				}
				foreach (FieldInfo field in t.GetFields(scope | BindingFlags.DeclaredOnly))
				{
					// Compiler-generated backing fields duplicate their property
					if (field.Name.Contains("k__BackingField"))
					{
						continue;
					}
					yield return field;
				}
			}
		}

		public static IEnumerable<PropertyInfo> Properties(Type type, BindingFlags scope)
		{
			var seen = new HashSet<string>();
			for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
			{
				foreach (PropertyInfo property in t.GetProperties(scope | BindingFlags.DeclaredOnly))
				{
					if (property.GetIndexParameters().Length == 0 && seen.Add(property.Name))
					{
						yield return property;
					}
				}
			}
		}

		public static IEnumerable<MethodInfo> Methods(Type type, BindingFlags scope)
		{
			for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
			{
				if (IsUnityEngineType(t) && t != type && (scope & BindingFlags.Public) == 0)
				{
					yield break;
				}
				foreach (MethodInfo method in t.GetMethods(scope | BindingFlags.DeclaredOnly))
				{
					if (!method.IsSpecialName)
					{
						yield return method;
					}
				}
			}
		}

		private static string Access(MemberInfo member)
		{
			bool isPublic = member switch
			{
				FieldInfo f => f.IsPublic,
				MethodInfo m => m.IsPublic,
				PropertyInfo p => (p.GetGetMethod(true) ?? p.GetSetMethod(true))?.IsPublic ?? false,
				_ => false,
			};
			return isPublic ? "public" : "private";
		}

		private static bool IsStaticMember(MemberInfo member)
		{
			return member switch
			{
				FieldInfo f => f.IsStatic,
				MethodInfo m => m.IsStatic,
				PropertyInfo p => (p.GetGetMethod(true) ?? p.GetSetMethod(true))?.IsStatic ?? false,
				_ => false,
			};
		}

		// "<kind> <static|instance> <public|private> <type> <name>[ (params)] [ro|rw]" per member
		public static List<string> ListMembers(Type type, BindingFlags scope, string filter)
		{
			var lines = new List<string>();
			bool Matches(string name) => string.IsNullOrEmpty(filter) || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
			string Scope(MemberInfo m) => IsStaticMember(m) ? "static" : "instance";

			foreach (FieldInfo field in Fields(type, scope).Where(f => Matches(f.Name)))
			{
				string mode = field.IsLiteral || field.IsInitOnly ? "ro" : "rw";
				lines.Add($"field {Scope(field)} {Access(field)} {TypeName(field.FieldType)} {field.Name} {mode}");
			}
			foreach (PropertyInfo property in Properties(type, scope).Where(p => Matches(p.Name)))
			{
				string mode = property.GetSetMethod(true) != null ? "rw" : "ro";
				lines.Add($"property {Scope(property)} {Access(property)} {TypeName(property.PropertyType)} {property.Name} {mode}");
			}
			foreach (MethodInfo method in Methods(type, scope).Where(m => Matches(m.Name)))
			{
				string parameters = string.Join(", ", method.GetParameters().Select(p => $"{TypeName(p.ParameterType)} {p.Name}"));
				lines.Add($"method {Scope(method)} {Access(method)} {TypeName(method.ReturnType)} {method.Name}({parameters})");
			}
			return lines;
		}

		// "<name> <type> = <value>" per field and readable property of one object, or of the
		// type's statics when target is null
		public static List<string> Inspect(object target, Type type)
		{
			BindingFlags scope = target == null ? AnyStatic : AnyInstance;
			var lines = new List<string>();

			foreach (FieldInfo field in Fields(type, scope))
			{
				string valueText;
				try
				{
					valueText = Format(field.GetValue(target), true);
				}
				catch (Exception e)
				{
					valueText = $"<error: {AutomationServer.DescribeException(e)}>";
				}
				lines.Add($"{field.Name} {TypeName(field.FieldType)} = {valueText}");
			}

			foreach (PropertyInfo property in Properties(type, scope))
			{
				MethodInfo getter = property.GetGetMethod(true);
				if (getter == null
					|| s_unsafeInspectProperties.Contains(property.Name)
					|| property.IsDefined(typeof(ObsoleteAttribute), true))
				{
					continue;
				}

				string valueText;
				try
				{
					valueText = Format(property.GetValue(target), true);
				}
				catch (Exception e)
				{
					valueText = $"<error: {AutomationServer.DescribeException(e)}>";
				}
				lines.Add($"{property.Name} {TypeName(property.PropertyType)} = {valueText}");
			}
			return lines;
		}

		// ---- Calls -------------------------------------------------------------------------

		// Calls the overload of the name whose parameters parse from the argument tokens. A
		// composite argument (a vector) is one quoted token: "1 2 3".
		public static bool TryCall(object target, Type type, string methodName, List<string> args, out object result, out bool returnsVoid, out string error)
		{
			result = null;
			returnsVoid = false;
			error = null;

			BindingFlags scope = target == null ? AnyStatic : AnyInstance | BindingFlags.Static;
			var candidates = new List<MethodInfo>();
			for (Type t = type; t != null; t = t.BaseType)
			{
				candidates.AddRange(t.GetMethods(scope | BindingFlags.DeclaredOnly)
					.Where(m => m.Name == methodName && !m.ContainsGenericParameters && m.GetParameters().Length >= args.Count));
			}

			if (candidates.Count == 0)
			{
				error = $"{type.Name} has no {(target == null ? "static " : string.Empty)}method '{methodName}' taking {args.Count} arguments";
				return false;
			}

			string lastError = null;
			foreach (MethodInfo method in candidates)
			{
				ParameterInfo[] parameters = method.GetParameters();
				var values = new object[parameters.Length];
				bool parsed = true;
				for (int i = 0; i < parameters.Length && parsed; ++i)
				{
					if (i < args.Count)
					{
						parsed = TryParseValue(parameters[i].ParameterType, args[i], out values[i], out lastError);
					}
					else if (parameters[i].HasDefaultValue)
					{
						values[i] = parameters[i].DefaultValue;
					}
					else
					{
						parsed = false;
						lastError = $"missing argument '{parameters[i].Name}'";
					}
				}

				if (!parsed)
				{
					continue;
				}

				returnsVoid = method.ReturnType == typeof(void);
				result = method.Invoke(method.IsStatic ? null : target, values);
				return true;
			}

			error = $"no overload of '{methodName}' accepts those arguments ({lastError})";
			return false;
		}
	}
}
