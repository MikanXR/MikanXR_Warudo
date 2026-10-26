# MikanXR Warudo Plugin

A [Warudo](https://warudo.app) plugin mod for [MikanXR](https://github.com/MikanXR/MikanXR). The project is the Warudo SDK 0.14.5.1 modding project with the mod tool moved to 0.15.2. It currently holds the KlakSpout feasibility spike: a plugin that publishes an animated test pattern to the Spout sender `MikanXR_WarudoSpike`.

## Layout

- `Assets/MikanXRSpike/`: the mod folder. Everything in it is compiled from source into the `.warudo` file.
- `Assets/HostAssemblies/`: Warudo assemblies the mod compiles against but does not ship, copied from the Warudo install by `CopyHostAssemblies.bat` (the DLLs are gitignored). The mod references `Klak.Spout.Runtime` at compile time and binds to the copy Warudo already loads.
- `Assets/Editor/ModBatchBuild.cs`: the command line entry point for Warudo > Build Mod.
- `Assets/ExportSettings.asset`: the mod tools export profile (mod folder, output folder `Mod/`).

## Setup

Requirements: Unity 2021.3.45f2 exactly, Git, and Warudo installed.

```bat
CopyHostAssemblies.bat "D:\Program Files (x86)\Steam\steamapps\common\Warudo"
```

The mod tool is pinned in `Packages/manifest.json` to a commit on the `upm/latest` branch of `HakuyaLabs/Warudo-Mod-Tool` ("Publish meta UPM latest from 0.15.2.2"). The plain version tags such as `0.15.2.2` hold the source layout without folder `.meta` files and do not import as a package. To move to a newer SDK, pin the matching "Publish meta UPM latest" commit and delete `Library` so the project reimports cleanly.

## Building

```bat
"C:\Program Files\Unity\Hub\Editor\2021.3.45f2\Editor\Unity.exe" -projectPath . -executeMethod ModBatchBuild.Build -logFile build.log
```

This writes `Mod\MikanXRSpike.warudo` and exits with code 0, or 1 when the file was not written. Run it without `-batchmode` and with the project closed in any other editor. The mod tools log "Build Result: Success" even when writing the file throws, so the exit code checks for the file instead.

If the build fails with "the mod tools settings are not present", the `Library` folder's asset search index is stale. The zip's prebuilt `Library` had this, and swapping the mod tool package in place can cause it. Delete `Library` and let the project reimport.

## Installing

Copy the `.warudo` file into `Warudo_Data\StreamingAssets\Plugins` under the Warudo install. A running Warudo reloads a plugin when its file is replaced.

## Mod restrictions

The mod tools check the compiled mod at export against `ModToolsSecurityRestrictions.asset`: no `System.IO`, `System.Reflection`, `System.Runtime.InteropServices` or P/Invoke. The check is by IL member reference, so it also rejects members declared on reflection types even when called through `System.Type`. Observed with the 0.14.5.1 and 0.15.2 tools, which gave the same results:

- rejected
	- `typeof(T).Name` (`System.Reflection.MemberInfo::get_Name`)
	- `Type.Assembly`
	- `System.IO.File` and the rest of `System.IO`
- accepted
	- a compile-time reference to `Klak.Spout.Runtime`
	- `GetType().FullName`, `Type.GetType(string)`, `Type.IsEnum`, `Enum.GetValues`
	- `Activator.CreateInstance(Type)`
	- Newtonsoft `JsonConvert` and `JObject`
	- `System.Net.WebSockets.ClientWebSocket`, `Task.Run`, `Encoding.UTF8`

Warudo 0.15.1 loaded mods built with both the 0.14.5.1 and 0.15.2 tools. The 0.15.2 restrictions add `Acornima` and `Jint` to the denied assemblies and no longer deny `Application.OpenURL`.
