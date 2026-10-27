# MikanXR Warudo Plugin

A [Warudo](https://warudo.app) plugin mod for [MikanXR](https://github.com/MikanXR/MikanXR), the counterpart of the [VNyan plugin](https://github.com/MikanXR/MikanXR_VNyan). It mirrors a running MikanXR editor into the Warudo scene: the editor's stages, anchors, stencils and cameras appear under a MikanXR root. Each Mikan camera renders Warudo's content with the editor's calibrated lens, cuts the character out behind the editor's stencils, catches its shadow on them, and publishes color, depth and shadow buffers to the editor over Spout.

The mod compiles the [MikanXR Unity package](https://github.com/MikanXR/MikanXR_Unity)'s runtime from source, less the two folders a Warudo mod cannot carry (`Bindings/Native` uses P/Invoke and `Scripts/FileSystem` uses `System.IO`), and publishes through the KlakSpout Warudo already loads.

## Layout

- `Assets/MikanXR/`: the mod folder. Everything in it is compiled from source into the `.warudo` file.
	- `Plugin/`: the Warudo side (the plugin, the MikanXR and stage anchor assets, the nodes, the settings provider and the shader source)
	- `Runtime/` and `Shaders/`: the package's runtime and shaders, copied by `SyncMikanRuntime.bat` and gitignored
- `Assets/HostAssemblies/`: Warudo assemblies the mod compiles against but does not ship, copied by `CopyHostAssemblies.bat` (gitignored). The mod references `Klak.Spout.Runtime` at compile time and binds to the copy Warudo already loads.
- `Assets/Editor/ModBatchBuild.cs`: the command line entry point for Warudo > Build Mod
- `Assets/ExportSettings.asset`: the export profile (mod folder `Assets/MikanXR`, output folder `Mod/`)
- `Tools/Automation/`: the Warudo automation channel, a development tool (see below)

## Setup

Requirements: Unity 2021.3.45f2 exactly, Git, Warudo installed, and a MikanXR_Unity checkout.

```bat
CopyHostAssemblies.bat "D:\Program Files (x86)\Steam\steamapps\common\Warudo"
SyncMikanRuntime.bat D:\Github\git-MikanXR\MikanXR_Unity\Packages\com.mikanxr.unity
```

`SyncMikanRuntime.bat` defaults to `MIKAN_UNITY_PACKAGE`, then a `git-MikanXR\MikanXR_Unity` checkout two folders up, and records the package commit it copied in `Assets/MikanXR/Runtime/source.txt`. Run it again whenever the package changes.

The mod tool is pinned in `Packages/manifest.json` to a commit on the `upm/latest` branch of `HakuyaLabs/Warudo-Mod-Tool` ("Publish meta UPM latest from 0.15.2.2"). The plain version tags such as `0.15.2.2` hold the source layout without folder `.meta` files and do not import as a package. To move to a newer SDK, pin the matching "Publish meta UPM latest" commit and delete `Library` so the project reimports cleanly.

## Building

```bat
"C:\Program Files\Unity\Hub\Editor\2021.3.45f2\Editor\Unity.exe" -projectPath . -executeMethod ModBatchBuild.Build -logFile build.log
```

This writes `Mod\MikanXR.warudo` and exits with code 0, or 1 when the file was not written. Run it without `-batchmode` and with the project closed in any other editor. The mod tools log "Build Result: Success" even when writing the file throws, so the exit code checks for the file instead.

If the build fails with "the mod tools settings are not present", the `Library` folder's asset search index is stale. The zip's prebuilt `Library` had this, and swapping the mod tool package in place can cause it. Delete `Library` and let the project reimport.

## Installing

Copy `Mod\MikanXR.warudo` into `Warudo_Data\StreamingAssets\Plugins` under the Warudo install. A running Warudo reloads a plugin when its file is replaced, and keeps the MikanXR assets already in the scene.

## Using

Add the **MikanXR** asset to a Warudo scene. It connects to the editor on this machine as client `Warudo`, and its status line shows the connection. Its transform is where the Mikan world sits, so Warudo's gizmo places it, and its settings save with the Warudo scene:

- **Connection:** the editor's host and port (blank for this machine and 8080), and a Reconnect button
- **World Scale:** Warudo units per Mikan meter, 10 by default, since Warudo characters are human sized and the editor's scenes are tabletop sized
- **Rendering:** stencil knockouts, shadows on the stencils, the depth buffer, the shadow strength, and which of Warudo's content the Mikan cameras render (characters, props, environment, screens). Warudo's UI, gizmos and per-camera layers never render.
- **Helpers:** the camera frustums, stencil previews and light cones in Warudo's own view
- **Lighting:** whether the editor's light environment and DMX spot lights light the scene, their intensity, and whether Warudo's own directional lights are hidden from the Mikan cameras

To place a stage, add a **MikanXR Stage Anchor** asset and pick the stage from the editor's stages. While the anchor exists, the stage sits where the anchor is and follows its gizmo, and the editor's own transform for that stage is ignored. Removing the anchor hands the stage back to the editor.

A Mikan camera's renders are lit by the light environment captured through that camera, as the scene's ambient and a key light, while Warudo's own view keeps Warudo's lighting. A camera without a capture renders under Warudo's lighting alone. Warudo's directional light still lights the character beside the captured key light, so with a capture in place turn on Hide Warudo Directional Lights to leave the key light as the only sun in the Mikan renders.

In the editor, the compositor that should carry Warudo's character uses `unity_client_shadow_graph.compgraph` with a client texture source named `Warudo`, the client id.

Blueprint nodes, under MikanXR:

- **On MikanXR Connected** and **On MikanXR Disconnected**
- **On MikanXR Scene Changed**, with the scene id and name, which fires when the editor's current scene changes and once on connect
- **Get MikanXR Scene**, with the current scene id, name, and whether the client is connected

To move the character per scene, wire On MikanXR Scene Changed into a node that branches on the scene name and loads that scene's character position.

The client works in both of Warudo's render pipelines. Switching the pipeline restarts it, so its cameras and stencil materials come back for the new one. In URP mode:

- The Mikan cameras render from inside URP's own render loop, through `UniversalRenderPipeline.RenderSingleCamera`, since `Camera.Render` draws nothing there.
- The stencils catch the shadow of the main light only, which during a Mikan render is the captured key light when there is one. The shadow distance and cascades are Warudo's URP settings.
- The depth buffer comes from URP's depth texture. Warudo's SSAO makes URP fill that in a depth prepass, so it holds the materials that have a depth pass, and not the stencils.
- A character loaded before the switch keeps materials built for the built-in pipeline, which URP does not draw, in Warudo's own view as well. Load the character again after switching.
- The URP form of the shadow catcher lives in `Assets/MikanXR/ShadersURP`. Its pass carries no LightMode tag: the mod project builds under the built-in pipeline, whose shader stripping removes every pass tagged for a scriptable pipeline.

## Automation

`Tools/Automation` is a loopback TCP command channel inside Warudo, run from Warudo's Playground, for testing the mod without driving Warudo's UI by hand. It reads and writes any field, creates and edits assets and blueprints, presses their buttons, saves the scene, switches the render pipeline and takes screenshots. Anything that can reach the port controls Warudo, so do not leave it installed in a Warudo you stream with.

```bat
Tools\Automation\install.bat
python Tools\Automation\warudo_automate.py "app info" "warudo asset create MikanXR" "warudo asset get MikanXR Status"
```

`Tools/Automation/automation.md` is the command reference.

## Mod restrictions

The mod tools check the compiled mod at export against `ModToolsSecurityRestrictions.asset`: no `System.IO`, `System.Reflection`, `System.Runtime.InteropServices` or P/Invoke. The check is by IL member reference, so it also rejects members declared on reflection types even when called through `System.Type`, such as `typeof(T).Name`. MikanXR_Unity's edit mode test holds the package runtime to these rules. Warudo 0.15.1 loads mods built with the 0.15.2 tools.

Mods cannot ship DLLs, asmdefs, ScriptableObjects or Resources folders. The mod loads its shaders by path through `ModHost.Assets`, and the KlakSpout resources come from Warudo's own copy.
