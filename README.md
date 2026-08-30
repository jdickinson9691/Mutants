# Chronomutants (working title)

A standalone, text-based Windows RPG inspired by the classic Major BBS door
game **Mutants!**. Single-player for v1, with all other "players" in the
world simulated as full NPC Mutants running the same rules, classes, and
restrictions as the human player.

## Status

Pre-production / design phase. No game code has been written yet — this
repository currently holds the research, game design document, tech stack
recommendation, and agent/role contracts that will drive implementation.

## Start here

- [`research/ORIGINAL_MUTANTS_RESEARCH.md`](research/ORIGINAL_MUTANTS_RESEARCH.md) —
  everything verifiable about the original *Mutants!* BBS door game, with
  sources, and an explicit list of what is **not** documented anywhere (which
  is where this project's original design begins).
- [`docs/GDD.md`](docs/GDD.md) — the game design document for this project,
  clearly marking which mechanics are historically sourced vs. original.
- [`docs/TECH_STACK.md`](docs/TECH_STACK.md) — recommended tech stack
  (C#/.NET 8) and why, plus the planned repo layout and milestone sequence.
- [`docs/AGENTS.md`](docs/AGENTS.md) — the project's agent/role contracts
  (planning, design, engine, UI, content, QA, packaging, docs), so work can
  be picked up consistently across sessions and contributors.

## Roadmap (see `docs/TECH_STACK.md` for detail)

1. Core domain model (classes, stats, Ions, items) + unit tests
2. Grid movement on a single level
3. Combat, loot drops, convert/sell/wield
4. NPC simulation loop
5. Stores (government + player-owned) and the Riblet economy
6. Multi-level time travel with scaling
7. Leaderboards + start screen + save/load
8. Windows installer packaging

## License / provenance

This is an original game inspired by, but not a copy of, the proprietary
*Mutants!* door game (Majorware/Majorsoft). No original source code or
copyrighted assets from that game are used here — see the research doc for
what's independently documented vs. newly designed.
