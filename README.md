# Dota x PEAK

A solo PEAK mod that pushes Dota-inspired heroes, abilities, gold and items directly into PEAK's climbing loop.

## Current prototype — 0.3.0

- Host: PEAK
- Solo-focused
- F1: hero selection
- 1: Pudge
- 2: Juggernaut
- Q: hero ability
- B: Dota shop
- E: Blink when purchased
- Gold: +5 every 45 seconds survived
- Pudge Q: geometry-targeted traversal hook with a short visual line and 4s cooldown
- Juggernaut Q: 3s Blade Fury state with 1.35x PEAK movement modifier
- Boots of Speed: 1.12x PEAK movement modifier
- Persistent hero, gold and item ownership through BepInEx config

## Technical direction

The mod deliberately uses PEAK as the host rather than modifying the Dota 2 client. Current PEAK community tooling exposes local-character and movement hooks such as `Character.localCharacter`, `Character.WarpPlayer(Vector3,bool)`, and `CharacterMovement.movementModifier`; the prototype now targets those game APIs instead of only moving a generic Unity transform.

## Build

The project follows the current PEAK BepInEx project layout and targets .NET SDK 10+.

1. Make sure PEAK + BepInEx are installed.
2. Copy `Config.Build.user.props.template` to `Config.Build.user.props` if the default Steam path is wrong.
3. Run `dotnet build -c Release`.
4. The Release build copies the DLL to PEAK/BepInEx/plugins/ when the local PEAK path exists.
5. Launch PEAK and inspect `BepInEx/LogOutput.log`.

## Verification status

The 0.1.0 DLL was already confirmed loading in PEAK. Version 0.3.0 contains the larger gameplay pass and still requires a fresh build + in-game verification before it is called playable.

## Design source of truth

The JSON files in `design/` define heroes, systems, items and preflight state.

## Compatibility

Target host is PEAK. This project does not modify the Dota 2 client or bypass Dota 2 anti-cheat.
