# Solar Expanse Launch Windows — Claude guidance

## Project layout

```
src/core/   Pure C# (netstandard2.0) — interfaces, WindowFinder, LWCacheHelper, AlarmKey, LWSaveTypes.
            No Unity dependencies. Referenced by both mod and tests.
src/mod/    BepInEx mod (net472). Unity-specific implementations (GameBodyEphemeris, GameLambertSolver,
            GameClock) and the panel/injector MonoBehaviours.
src/tests/  NUnit test project (net10.0). References core only — no game DLLs required.
```

## Testing policy

**Always TDD.** Write the test first, watch it fail, then implement.

- Any logic that can be expressed as a pure function belongs in `src/core/` and must have tests.
- Mock Unity/game dependencies via the interfaces (`IBodyEphemeris`, `ILambertSolver`, `IGameClock`);
  see `src/tests/Stubs.cs` for the existing fakes.
- MonoBehaviour lifecycle, Unity singletons, and `JsonUtility` serialisation are the only things
  not worth unit-testing. Everything else should be tested.
- Run tests before committing: `dotnet test src/tests/`.

## Building and installing

```bash
bash build.sh            # builds Release and copies DLL to game's BepInEx/plugins/
dotnet test src/tests/   # run the unit tests
```

Override the game path: `SOLAR_EXPANSE_GAME=/path/to/Solar\ Expanse bash build.sh`

## Key invariants

- **Game constants are copied exactly** from the decompiled source at
  `extract/cache/project/ExportedProject/Assets/Scripts/Assembly-CSharp/`. Never approximate.
- **`dvToKmS`** conversion: `ge.timeScale / (0.21094953 * ge.lengthScale)` — same formula the game uses.
- **Window cache promotion**: when opt1 is stale but opt2 is still valid, promote opt2→opt1 and
  schedule one `FindWindows` scan starting at `opt1.DepartureEpoch + synodic − 30 days` for the
  new opt2. `LWCacheHelper.PromoteWindowCache` owns this logic.
- **Sidecar files** live at `<plugin-dir>/saves/<savename>.lw.json`, written on save, read on load
  via `ISaveStateDataProvider`.
