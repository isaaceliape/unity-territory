# Unity client source (Phase 3, in progress)

These files are the Unity half of the conversion. They need the Unity editor
to compile and run — plain `dotnet` cannot build them (UnityEngine + UGUI +
Newtonsoft assemblies only exist in the editor).

## How it assembles in the editor

1. Create a Unity 6 project (Universal 2D or Built-in Render Pipeline both work).
2. Copy this whole `unity-territory` tree next to it, or copy the folders in:
   - `src/Territory.Sim/*.cs` -> `Assets/Scripts/Sim/` (pure C#, no Unity refs).
     Same files the .NET suite tests; add them to an `Assembly-CSharp`-adjacent
     asmdef or just drop them in. No changes needed.
   - `src/Territory.Client/*.cs` -> `Assets/Scripts/Client/Core/` (BoardModel,
     ViewHelpers, SfxSynth, FxModel — also engine-free).
   - `Assets/Scripts/...` (this folder) -> `Assets/Scripts/` as-is.
3. Install the Newtonsoft.Json package (`com.unity.nuget.newtonsoft-json`)
   — `GameClient`/`HudController` parse the wire protocol with it.
4. Build a scene with: `GameClient` + `UnityWsClient` (+ `TerritoryWsBridge`
   named exactly `TerritoryWs`), `BoardRenderer` (map quad + minimap RawImage),
   `HudController` (wired UGUI), `InputController`, `SfxPlayer`, `FxRenderer`.
5. WebGL build output goes into the C# server's `wwwroot`
   (`Territory.Server --wwwroot <build dir>`). The server already serves
   `.wasm`/`.js`/`.data` with the right MIME types and pre-compressed `.gz`.

## Files

- `Scripts/Net/UnityWsClient.cs` — socket with auto-reconnect. Standalone uses
  ClientWebSocket; WebGL goes through the `.jslib` bridge.
- `Scripts/Net/TerritoryWsBridge.cs` — keeps the callback GameObject named
  `TerritoryWs` so `SendMessage` from JS finds it.
- `Plugins/WebGL/TerritoryWs.jslib` — browser WebSocket in JS.
- `Scripts/Client/GameClient.cs` — protocol <-> model orchestration. Same
  message flow as `public/client.js` (join/hello/lobby/full/tick/over,
  capture/death celebration, countdown GO, win/lose sounds).
- `Scripts/Client/BoardRenderer.cs` — one texture for the board (same
  rounded-corner + diagonal-stitch rules as `drawRoundedMap`), head sprites
  with name labels, follow/whole-map camera.
- `Scripts/Client/HudController.cs` — cards, scoreboard, clock, minimap,
  kill feed, buttons, touch visibility.
- `Scripts/Client/InputController.cs` — keyboard + hold buttons + cursor
  steer (W/S do not steer, like the browser game).
- `Scripts/Client/SfxPlayer.cs` — one-shot clips rendered from `SfxSynth`
  recipes; the theme renders once to a looping clip. Separate Sound/Music
  mutes in PlayerPrefs.
- `Scripts/Client/FxRenderer.cs` — quad-mesh particles/flashes, LineRenderer
  rings, pooled TextMesh floaters, driven by `FxModel`.

## Still to do in the editor (cannot be done headless)

- Scene/prefab wiring for all the `[Header]` references.
- First WebGL build + playtest pass (PRD acceptance: wall glow, feed,
  danger cue, strip, sound, touch, wins).
- Minimap + glow polish, font/art pass.
- `prefers-reduced-motion` equivalent: Unity reads it from the page in the
  WebGL template and sets `FxModel.ReducedMotion`.
