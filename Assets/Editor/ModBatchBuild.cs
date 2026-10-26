using System;
using System.IO;
using UMod.BuildEngine;
using UMod.ModTools.Export;
using UMod.Shared;
using UnityEditor;
using UnityEngine;

// Builds the active export profile from the command line, the same as Warudo > Build Mod:
// Unity.exe -projectPath <repo> -executeMethod ModBatchBuild.Build
// Run without -batchmode: the build waits for the editor to tick a few times, so the mod tools
// have loaded their settings, and the editor exits with code 0 when the .warudo file was written
// and 1 otherwise. The mod tools report success even when writing the file throws, so the file
// is what counts.
public static class ModBatchBuild
{
	private const int k_waitTicks = 30;
	private const string k_modFileExtension = ".warudo";
	private static int s_ticks;

	public static void Build()
	{
		s_ticks = 0;
		EditorApplication.update += WaitThenBuild;
	}

	private static void WaitThenBuild()
	{
		if (++s_ticks < k_waitTicks || EditorApplication.isCompiling || EditorApplication.isUpdating)
		{
			return;
		}

		EditorApplication.update -= WaitThenBuild;

		int exitCode = 1;
		try
		{
			exitCode = BuildActiveProfile() ? 0 : 1;
		}
		catch (Exception e)
		{
			Debug.LogException(e);
		}

		Debug.Log($"ModBatchBuild: finished with exit code {exitCode}");
		EditorApplication.Exit(exitCode);
	}

	private static bool BuildActiveProfile()
	{
		ExportSettings settings = ExportSettings.Active.Load();
		if (settings == null || settings.ActiveExportProfile == null)
		{
			Debug.LogError("ModBatchBuild: no active export profile in this project");
			return false;
		}

		// The mod tools compile the mod's scripts from the generated project file
		if (!File.Exists("Assembly-CSharp.csproj"))
		{
			Unity.CodeEditor.CodeEditor.CurrentEditor.SyncAll();
		}

		ExportProfileSettings profile = settings.ActiveExportProfile;
		Directory.CreateDirectory(profile.ModExportPath);
		string modFile = Path.Combine(profile.ModExportPath, profile.ModName + k_modFileExtension);
		if (File.Exists(modFile))
		{
			File.Delete(modFile);
		}

		Debug.Log($"ModBatchBuild: building {profile.ModName} to {modFile}");
		ModToolsUtil.StartBuild(settings);

		if (!File.Exists(modFile))
		{
			Debug.LogError($"ModBatchBuild: the build did not write {modFile}");
			return false;
		}

		return true;
	}
}
