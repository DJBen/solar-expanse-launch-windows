# Contributing

## Building from source

Requires .NET SDK and the game installed locally.

```bash
git clone https://github.com/stockmaj/solar-expanse-launch-windows
cd solar-expanse-launch-windows
bash build.sh          # build and install into your local game
dotnet test src/tests/ # run the unit tests (no game required)
```

`build.sh` compiles the mod and copies the single DLL directly into your local
game's `BepInEx/plugins/` folder. Set `SOLAR_EXPANSE_GAME=<path>` to override
the default game location.

## Project layout

```
src/mod/    The BepInEx plugin. Multi-targets net472 (full build, Unity references)
            and netstandard2.0 (pure-C# logic only, for tests). Unity-dependent
            files are excluded from the netstandard2.0 build.
src/tests/  NUnit tests (net10.0). Reference the mod project and get the
            netstandard2.0 output — no game DLLs required to run them.
```

## Testing policy

Any logic expressible as a pure function belongs in `src/mod/` alongside the
interfaces (`IBodyEphemeris`, `ILambertSolver`, `IGameClock`) and must have
tests. See `src/tests/Stubs.cs` for the existing fakes. MonoBehaviour lifecycle,
Unity singletons, and `JsonUtility` serialisation are the only things not worth
unit-testing.

Always write the test first, watch it fail, then implement.
