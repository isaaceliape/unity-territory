# Unity Territory

Full Unity-engine conversion of [territory](../territory) (local multiplayer
browser game: own land, drive a snake, close loops). One Unity project builds
both a **headless dedicated server** and a **WebGL client**. Plan:
`../territory/docs/PRD-unity-conversion.md`.

> No Unity editor is installed in this environment yet, so Phase 1 is plain
> .NET and Phase 3 is source-only until an editor is available. Nothing here
> depends on UnityEngine, by design.

## Layout

```
src/Territory.Sim/        pure C# game rules (port of territory/server/game.js)
tests/Territory.Sim.Tests/ NUnit port of territory/test/rules.test.js
src/Territory.Server/     Phase 2: headless host (sim loop + WebSocket + HTTP)
src/Territory.Client/     Phase 3: Unity WebGL scripts (rendering, input, HUD)
```

`Territory.Sim` targets `netstandard2.0` so the same files drop straight into
`Assets/Scripts/Sim/` in Unity. It must never reference UnityEngine.

## Phase 1 status: done

`Game` mirrors `createGame`: grid `Land`/`Trail` arrays, 110ms tick, phases
lobby/countdown/play/over, 12 seats, Easy/Normal/Hard bots with the same
`BOT_LEVELS` numbers, seeded `Random` (`GameOptions.Random`, mulberry32 in
tests so bot paths match the JS game tick for tick). Per-tick write sets
(`LandRuns`/`TrailWrites`) and `MsLeftAt` feed the wire protocol.

## Phase 2 status: done

`Territory.Server` is the headless host: dependency-free WebSocket +
static-file server on one port (`WsServer.cs`), session logic ported from
`server/index.js` (`GameHost.cs`: seating, tokens, 10s reconnect grace,
host-only actions, session wins, results log, tick loop with snapshot/delta
broadcasts), and `Program.cs` with the same one-command story
(`PORT=4173`, prints LAN addresses). The JSON shapes match the Node server,
verified live: the original Node client joined the C# server, added a hard
bot, started a round, and received valid snapshots + deltas.

## Phase 3 status: client core done, Unity shell source-only

`Territory.Client` ports the engine-free client logic with tests: `ViewHelpers`
(feed text, wall warnings, danger cells, wins labels, clock), `BoardModel`
(snapshots, deltas, interpolated poses, scoreboard order), `SfxSynth` (every
effect recipe + the 32-step theme rendered to PCM), `FxModel` (particles,
rings, flashes, floaters, confetti, shake). `src/Territory.Client.Unity/`
holds the Unity scripts (socket, board/HUD/input/audio/effects, WebGL
`.jslib`) plus an editor setup README — they need the Unity editor to
compile (UnityEngine/UGUI/Newtonsoft assemblies).

## Cloud builds (GameCI)

`.github/workflows/build.yml` builds WebGL on every push to main and
uploads the `territory-webgl` artifact. Before the first green build, do the
one-time license setup:

1. Actions tab → "Request activation file" → Run workflow.
2. Download the `activation-file` artifact (a `.alf` file).
3. Upload it at <https://license.unity3d.com/manual> while signed in.
4. Download the resulting license file and store its **contents** as the
   `UNITY_LICENSE` repo secret, plus `UNITY_EMAIL` and `UNITY_PASSWORD`
   (your Unity ID). Settings → Secrets → Actions.
5. Re-run the build workflow.

The workflow copies `src/` into `unity-project/Assets` before building, so
`src/` stays the single source of truth.

## Build and test (.NET side)

Needs .NET SDK 10 (`dotnet --version`). With the SDK on PATH:

```
dotnet test
```

28 tests, all ported from `test/rules.test.js` with the same scenario names
and the same exact numbers (land counts, scores, bot ordering). Any failure
means the C# sim drifted from the JS rules.
