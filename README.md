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
tests so bot paths match the JS game tick for tick).

## Build and test

Needs .NET SDK 10 (`dotnet --version`). With the SDK on PATH:

```
dotnet test
```

28 tests, all ported from `test/rules.test.js` with the same scenario names
and the same exact numbers (land counts, scores, bot ordering). Any failure
means the C# sim drifted from the JS rules.
