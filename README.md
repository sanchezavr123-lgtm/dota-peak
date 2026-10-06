# Dota x PEAK

A solo-first PEAK mod that puts Dota-style heroes, abilities, gold and items directly into PEAK's climbing loop.

## Current development target — 1.1.0

- Host: PEAK
- Hero choice appears at the start of each detected run; no F1 hero menu.
- Pudge: Z — Meat Hook
- Tiny: Z — Toss
- B: Dota Shop, opened from the lower-left shop control
- C: Blink when purchased
- X: Sell nearby PEAK loot
- Gold: +5 every 45 seconds survived, plus sellable PEAK loot caches
- Boots of Speed: +12% PEAK movement
- Boots and Blink are run-local purchases and reset when a new run is detected.
- No persistent top HUD.

## Why the controls changed

PEAK's default controls include Q for drop/throw, E for interact, R for emotes, and V for push-to-talk, so the Dota layer now uses Z/C/X for ability, Blink, and loot sale respectively. citeturn146457search0turn146457search6

## Technical direction

The mod uses PEAK as the host rather than modifying the Dota 2 client. The plugin is loaded through PEAK's BepInEx setup and uses PEAK-native character APIs for movement. PEAK's modding documentation recommends the BepInEx 5 project route for C# mods. citeturn507641search0turn507641search2

## Build

1. Make sure PEAK + BepInEx are installed.
2. Copy `Config.Build.user.props.template` to `Config.Build.user.props` if the default Steam path is wrong.
3. Run `dotnet build -c Release`.
4. The Release build copies the DLL to PEAK/BepInEx/plugins/ when the local PEAK path exists.
5. Launch PEAK and inspect `BepInEx/LogOutput.log`.

## Verification

The repository contains a v1.1 gameplay/UI pass, but the fresh DLL has not yet been rebuilt and verified in-game after these changes. The next verification is a clean Release build followed by a new PEAK run.

## Design source of truth

The JSON files in `design/` define heroes, systems, items and preflight state.

## Compatibility

Target host is PEAK. This project does not modify the Dota 2 client or bypass Dota 2 anti-cheat.
