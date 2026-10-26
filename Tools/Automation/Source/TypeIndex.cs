using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace WarudoAutomation
{
	// Every type in the loaded assemblies, found by full name or by a simple name that is unique.
	// Rebuilt when the assembly count changes, since plugins load after this one.
	public static class TypeIndex
	{
		private static List<Type> s_types;
		private static int s_assemblyCount;

		public static List<Type> AllTypes
		{
			get
			{
				Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
				if (s_types == null || assemblies.Length != s_assemblyCount)
				{
					s_assemblyCount = assemblies.Length;
					s_types = new List<Type>();
					foreach (Assembly assembly in assemblies)
					{
						s_types.AddRange(TypesOf(assembly));
					}
				}
				return s_types;
			}
		}

		public static IEnumerable<Type> TypesOf(Assembly assembly)
		{
			try
			{
				return assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException e)
			{
				return e.Types.Where(t => t != null);
			}
			catch (Exception)
			{
				return Enumerable.Empty<Type>();
			}
		}

		public static bool TryResolve(string name, out Type type, out string error)
		{
			type = null;
			error = null;

			var exact = new List<Type>();
			var simple = new List<Type>();
			var simpleIgnoreCase = new List<Type>();
			foreach (Type candidate in AllTypes)
			{
				if (candidate.FullName == name)
				{
					exact.Add(candidate);
				}
				else if (candidate.Name == name)
				{
					simple.Add(candidate);
				}
				else if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
				{
					simpleIgnoreCase.Add(candidate);
				}
			}

			foreach (List<Type> matches in new[] { exact, simple, simpleIgnoreCase })
			{
				if (matches.Count == 1)
				{
					type = matches[0];
					return true;
				}
				if (matches.Count > 1)
				{
					error = $"type '{name}' is ambiguous: " +
						string.Join(", ", matches.Select(t => $"{t.FullName} ({t.Assembly.GetName().Name})"));
					return false;
				}
			}

			error = $"no type '{name}'";
			return false;
		}
	}
}
