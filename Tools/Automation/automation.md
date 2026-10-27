# Warudo automation

How to drive and inspect a running Warudo over the automation scripts: a loopback TCP text channel for test scripts and AI tooling, adapted from VNyanAutomationPlugin. The scripts run from Warudo's Playground, which compiles loose `.cs` files from `Warudo_Data\StreamingAssets\Playground` with no mod sandbox and hot-reloads them on every change. The server lives in `Source/AutomationServer.cs` on a hidden `DontDestroyOnLoad` object and pumps its socket from `Update`, so every command runs on Unity's main thread against live scene state. `Source/WarudoAutomationPlugin.cs` is the Playground plugin that starts it.

---

## Installing and port

`install.bat [WarudoDir]` copies `Source/*.cs` into `Playground\WarudoAutomation`, and `uninstall.bat` removes that folder. The other Playground scripts are left alone, but they compile together with these, so a broken script there stops this one loading too (`log file` and `Player.log` show the Roslyn errors).

The server listens whenever the scripts are installed, on 127.0.0.1 only. The default port is 21140, clear of the MikanXR editor's automation (21120), VNyan automation (21130), Lua debugger (21110) and websocket (8080) ports. `-automationPort=<n>` on Warudo's command line overrides it.

The channel can read and write any field, call any method, and create and change any Warudo entity, so any local process that reaches the port owns the Warudo session. Leave the scripts out of a Warudo install that streams for real.

An edit to an installed script recompiles in under a second. Warudo destroys the old plugin and creates the new one in the same frame, so the plugin closes the listener itself before Unity destroys the server, and a new server that still finds the port busy retries each second. Copying several files fires several compiles, and an intermediate one can see a half-updated set and fail before the last one succeeds.

## Protocol

The MikanXR editor's automation protocol, byte for byte, so one client drives both. One client at a time, and a second connection waits in the backlog until the first closes. Commands are newline-terminated text lines (an optional trailing `\r` is stripped). Tokens split on spaces and tabs, and a double-quoted span groups spaces into one token, with `\"` and `\\` escapes inside quotes.

Every command answers with one framed reply: a line holding the reply's content line count, then exactly that many content lines, each CRLF terminated. A success with nothing to say is just `0`. An error is `1` then one line `<namespace> <verb>: <message>`. An unknown namespace replies naming the valid namespaces, in sorted order. An empty line answers `0`.

A handler that needs the end of the frame (the screenshots, a scene save) defers its reply, and the server reads no further command until that reply goes out, so replies stay in command order.

## Targets

The `go`, `obj` and `screenshot` commands take a target token:

- `#<instanceId>` is any live Unity object by instance id, as every listing prints it

- `/Root/Child` is a GameObject by hierarchy path from a root of any loaded scene, `DontDestroyOnLoad` included. The first match wins when siblings share a name, so use the `#id` for those.

- `@<TypeName>` is the first live object of a Unity type

- `$<asset>` is a Warudo asset by id, name or type title. An asset with a GameObject resolves to it (`$Character`, `$MikanXR`). Any other asset resolves to the asset entity itself, for `obj`.

- Any of these followed by `:<ComponentType>` picks a component on the GameObject, matched by the component's own type name or a base type's, with `[n]` for the nth of several (`$MikanXR:MikanClient`, `#1234:Transform`)

Quote a target holding spaces: `"/MikanXR/Mikan World/STG_Desk"`.

## Commands

`help` lists every registered command.

### App (app)

- `app info` replies `product`, `version`, `unity`, `plugin`, `port`, `frame`, `time`, `fps`, `screen`, `scenes`, `warudo_scene`, `assets`, `pipeline`, `log` (the `Player.log` path) and `data` (the StreamingAssets folder) lines
- `app assemblies [filter]` replies `<name> <version> <location>` per loaded assembly
- `app quit` replies, then quits Warudo through `Application.Quit` on the next frame. Warudo can hold the quit, for instance after a plugin setting changed, so a drive falls back to `kill`.

### Log (log)

The scripts keep the last 4000 Unity log messages from every thread, each numbered with a sequence. They hook the log when the plugin starts, so `log file` reaches what came before.

- `log tail <lineCount> [minLevel]` replies the most recent entries at or above the level, oldest first
- `log since <sequence> [minLevel]` replies every entry after a sequence number
- `log mark [text...]` writes a marker line through `Debug.Log` and replies its sequence number. A drive marks, acts, then reads `log since` the mark to see only what the action caused.
- `log clear` empties the ring
- `log file <lineCount>` replies the tail of `Player.log` itself

Entries read `<sequence> <HH:mm:ss.fff> <level> <message>`. Levels are `info`, `warning` and `error`, with asserts and exceptions counted as errors. Exceptions print as `exception` and carry their stack trace as indented lines.

### Scene hierarchy (go)

GameObjects reply as `#id /path <active|hidden|inactive>` (`hidden` is active itself under an inactive parent).

- `go scenes` replies `<name> roots=<n> path=<path>` per loaded scene
- `go roots [sceneName]` lists scene roots, all scenes when none is named
- `go children <target>` lists direct children
- `go tree <target> [depth]` replies an indented subtree, three levels by default
- `go find <nameSubstring> [all]` finds scene GameObjects by name, active ones unless `all`, up to 200
- `go components <target>` replies `#id <TypeFullName> <enabled|disabled|->` per component
- `go active <target> [true|false]` sets activeSelf when given, then replies `self` and `hierarchy` lines
- `go transform <target>` replies the transform's id, local and world position, rotation, Euler angles and scale, and its parent

### Reflection (obj, static, type)

Members are found on the type and its bases, public and private, fields and properties alike. A member path is dotted, with `[n]` to index a list or array and `[key]` to look up a dictionary entry. A set through a struct writes each struct back into its holder, so `obj set #1234:Transform position.y 2` moves the transform.

- `obj get <target> [member.path]` replies the value
- `obj set <target> <member.path> <value...>` parses the value tokens to the member's type and assigns it
- `obj call <target> <method> [args...]` calls the overload whose parameters parse from the arguments, filling trailing defaulted parameters, and replies the return value (nothing for void). A method returning `IEnumerator` on a MonoBehaviour is started as a coroutine.
- `obj inspect <target> [member.path]` replies `<name> <type> = <value>` for every field and readable property of the target (or of the value at the path)
- `obj members <target> [filter]` replies `<field|property|method> <static|instance> <public|private> <type> <name> [rw|ro]` per member, filtered by a name substring
- `static get|set|call <Type> ...` are the same over a type's static members, and `static inspect <Type>` lists its static values
- `type find <nameSubstring> [assemblySubstring]` replies `<FullName> <assembly> <kind>` per loaded type, up to 200
- `type members <Type> [filter]` lists a type's members with its base type
- `type instances <Type>` lists the live objects of a Unity object type

A type is named by its full name or by a simple name unique across the loaded assemblies. Hot reload leaves a mod's or a Playground script's earlier assemblies loaded, so copies of one type resolve to the newest. A setting on a Warudo entity is better changed through `warudo`, which runs the entity's watchers, than through `obj set`, which writes the field behind Warudo's back.

Value syntax for set and call arguments:

- booleans are `true`/`false` (also `1`/`0`, `on`/`off`)
- enums take a name, a number, or flags joined with `|`
- `Vector2`/`Vector3`/`Vector4` take one number per component, `Quaternion` takes `x y z w` or three Euler angles in degrees, `Color` takes `r g b [a]`, `Rect` takes `x y width height`
- a Unity object reference takes a target token, or `null`
- strings with spaces are quoted in a call argument. A set joins its remaining tokens with single spaces.
- any other type with a static `Parse(string)` parses through it

Values format as space-separated components for vectors, `#id Type /path` for Unity objects, `[count] [first 20 elements]` for collections, and `{FullTypeName}` for a type without its own `ToString`.

### Warudo (warudo)

Assets, plugins and blueprint nodes are all Warudo entities: their settings are data input ports and their buttons are trigger ports, so the same verbs read, write and press them. Writes go through Warudo's own setters with a broadcast, so the entity's watchers run and the editor UI follows, the same as a user edit.

- `warudo assets` replies `<id> "<type title>" "<name>" <active|inactive>` per asset in the open scene
- `warudo asset types [filter]` replies `<typeId> "<title>" <TypeFullName>` per registered asset type
- `warudo asset create <typeTitle|typeId> [name]` adds an asset and replies it, the same as the editor's Add Asset
- `warudo asset destroy <asset>` removes it
- `warudo asset ports <asset>` replies `data <key> = <value>` per data input and `trigger <key>` per trigger
- `warudo asset get <asset> <port.path>` replies a data input's value, with a dotted path into a structured value
- `warudo asset set <asset> <port.path> <value...>` parses the value to the port's type (the value syntax above) and sets it
- `warudo asset setjson <asset> <port.path> <json...>` sets a value from Warudo's own serialized form, for structured types
- `warudo asset trigger <asset> <trigger>` presses a trigger button
- `warudo plugins` replies `<pluginId> <TypeFullName>` per plugin, and `warudo plugin ports|get|set|setjson|trigger <pluginId> ...` work on a plugin's ports
- `warudo plugin save` writes every plugin's settings, which a scripted quit does not
- `warudo scene info` and `warudo scene save` (the open scene, through Warudo's own save)
- `warudo pipeline get` and `warudo pipeline set <builtin|urp>`
- `warudo graphs` replies `<id> "<name>" <enabled|disabled> nodes=<n>` per blueprint
- `warudo graph create <name>`, `warudo graph destroy <graph>` and `warudo graph enable <graph> <true|false>`
- `warudo graph nodes <graph>` and `warudo graph node types [filter]`
- `warudo graph node add <graph> <nodeTitle|nodeTypeId>` adds a node and replies its id
- `warudo graph node ports|get|set|setjson|trigger <graph> <node> ...` work on a node's ports
- `warudo graph flow <graph> <fromNode> <fromPort> <toNode> <toPort>` and `warudo graph data ...` connect ports

An `<asset>` is an id, a name or a type title, a `<graph>` an id or a name, and a `<node>` an id or a node title. Warudo's built-in titles are localization keys (`CAMERA`, `LOG_MESSAGE`). A blueprint built this way checks that an event node fires: wire it into a `LOG_MESSAGE` node and read the log.

### Screenshots (screenshot)

- `screenshot window [path]` writes the window as presented at the end of the frame, and replies the absolute path. Default `warudo_window.png` in Warudo's working directory.
- `screenshot camera <target> [path]` reads the texture a camera renders into as it last rendered (what Spout publishes), without rendering it again, and replies the path and the texture's size and name. Default `warudo_camera.png`. A camera drawing to the screen has nothing of its own to read and answers an error, and the window capture is its picture.
- `screenshot cameras` replies `#id /path <enabled|disabled> depth=<n> texture=<WxH|screen>` per camera

Camera textures keep their alpha, so a camera that sees nothing but background writes a fully transparent image.

## Client helper

`warudo_automate.py` is the client, a superset of the MikanXR editor's `tools/automate.py`: it connects (retrying a refused port until `--wait` seconds, default 20), sends each argument as one command, prints each framed reply, and exits nonzero on a connection failure, a timeout, or a failed step. `--port` overrides 21140, `--delay` sets the pause between commands (default 0.1 s), `--timeout` bounds each reply and `until` poll (default 60 s), and `--warudo` names the install (default `WARUDO_DIR`, then the Steam library path).

Client-side arguments:

- `ready` waits until the server answers `app info`
- `sleep:<seconds>` pauses
- `until:<command>:<expected>` polls the command until a reply line starts with the expected text
- `launch` starts `Warudo.exe` unless it is running (passing `-automationPort` for a non-default port), then waits up to `--launch-wait` (120 s) for the server to answer and prints `app info`. Steam must be running.
- `quit` sends `app quit` and waits up to `--quit-wait` (30 s) for the process to exit
- `kill` terminates `Warudo.exe` without its shutdown save
- `restart` is `quit` then `launch`
- `run:<command>` runs a shell command and fails the drive when it exits nonzero

The server answers once Warudo has compiled Playground, before the scene has loaded, so a drive that needs the scene polls for it: `"until:app info:warudo_scene Default"`.

## The drive and verify loop

A mod change, synced, built, installed and checked, from the MikanXR_Warudo checkout. A running Warudo reloads a plugin whose `.warudo` file is replaced, so no restart is needed:

```
SyncMikanRuntime.bat
"C:\Program Files\Unity\Hub\Editor\2021.3.45f2\Editor\Unity.exe" -projectPath . -executeMethod ModBatchBuild.Build -logFile build.log
copy /y Mod\MikanXR.warudo "D:\Program Files (x86)\Steam\steamapps\common\Warudo\Warudo_Data\StreamingAssets\Plugins\"
python Tools\Automation\warudo_automate.py sleep:5 "log tail 20 warning" "warudo asset get MikanXR Status"
```

To see what one action caused, mark the log first and read `log since` the sequence number the mark replied.
