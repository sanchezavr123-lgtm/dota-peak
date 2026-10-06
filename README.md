# Dota x PEAK

A solo PEAK mod mixing Dota-inspired heroes and abilities into PEAK's climbing loop.

## Current MVP

- Host: PEAK
- Solo-focused
- F1 opens hero selection
- 1 selects Pudge
- 2 selects Juggernaut
- Q activates the selected hero ability
- Pudge Hook prototype uses world geometry as a traversal target
- Juggernaut Blade Fury is currently a timed ability-state prototype

## Build

This project follows the current PEAK BepInEx project layout. The PEAK template requires .NET SDK 10+ and supports local game assembly references. PEAK currently does not have a GameLibs package suitable for GitHub Actions builds.

1. Install .NET SDK 10+.
2. Copy Config.Build.user.props.template to Config.Build.user.props if the default Steam path is not correct.
3. Run dotnet build -c Release from the repository root.
4. The Release build deploys DotaPeak.dll to PEAK/BepInEx/plugins/.
5. Launch PEAK and check BepInEx/LogOutput.log for DotaPeak 0.1.0 loaded.

## Design source of truth

The JSON files in design/ define the intended heroes, systems, items, and preflight checks.

## Compatibility

Target host is PEAK. This project does not modify the Dota 2 client or bypass Dota 2 anti-cheat.
