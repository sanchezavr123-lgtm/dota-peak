# Dota × PEAK

A solo PEAK mod that mixes Dota-style heroes, abilities, gold, items and shop progression into the climbing/survival loop.

## Current design

- Host game: PEAK
- Mode: Solo
- Core loop: climb, survive, collect resources/gold, buy upgrades, reach the top
- Hero layer: selectable Dota-inspired heroes with recognizable abilities
- Progression: gold + shop + items
- Design goal: a deliberately messy crossover where Dota mechanics interact with PEAK movement and traversal

## Status

Prototype planning stage. No playable release yet.

## Development rules

The JSON sheets in `design/` are the source of truth. Code should be generated/implemented from those sheets and every referenced value must be verified before a build.

## Safety / compatibility

This project targets PEAK as the host game. It does not modify the Dota 2 client or bypass Dota 2 anti-cheat.
