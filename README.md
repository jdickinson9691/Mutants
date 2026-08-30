# Chronomutants (working title)

A standalone, text-based Windows RPG inspired by the classic Major BBS door
game **Mutants!**. Single-player for v1, with all other "players" in the
world simulated as full NPC Mutants running the same rules, classes, and
restrictions as the human player.

## Status

All 8 milestones of the planned sequence (see Roadmap below) are built:
a playable console game with combat, loot, an NPC economy, multi-level
time travel, save/load with leaderboards, and a Windows installer build/
release pipeline. Content is data-driven (`src/Mutants.Content/*.json`,
loaded by `Mutants.Engine.Content.ContentLoader`) rather than hardcoded —
5 real levels, a full item/monster catalog, store catalogs, and complete
per-class ability tables; see `docs/CONTENT_PLAN.md` for exactly what's
shipped versus still open (levels 6–8, and ability *execution* — the
tables exist as data but nothing in combat uses them yet).

## Building & running

```
dotnet build Mutants.sln
dotnet test Mutants.sln
dotnet run --project src/Mutants.Console
```

To produce the distributable Windows build locally (requires
[Inno Setup 6](https://jrsoftware.org/isinfo.php)):

```
dotnet publish src/Mutants.Console -c Release -r win-x64 -o publish/win-x64
iscc installer/Chronomutants.iss
```

The installer lands in `installer/Output/`. Pushing a `v*` tag (e.g.
`git tag v0.1.0 && git push origin v0.1.0`) runs the same steps in CI and
attaches the installer to a GitHub Release automatically — see
[`.github/workflows/release.yml`](.github/workflows/release.yml).

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
- [`src/Mutants.Console/Program.cs`](src/Mutants.Console/Program.cs) — the
  playable console app; its file header notes exactly what's real vs.
  still simplified at any given point (e.g. NPCs currently only roam
  time-travel level 1 — full multi-level NPC simulation is flagged there
  as follow-up work).
- [`docs/CONTENT_PLAN.md`](docs/CONTENT_PLAN.md) — what's actually in
  `src/Mutants.Content/*.json` today, and what's still open.

## Roadmap (see `docs/TECH_STACK.md` for detail)

1. ✅ Core domain model (classes, stats, Ions, items) + unit tests
2. ✅ Grid movement on a single level
3. ✅ Combat, loot drops, convert/sell/wield
4. ✅ NPC simulation loop
5. ✅ Stores (government + player-owned) and the Riblet economy
6. ✅ Multi-level time travel with scaling
7. ✅ Leaderboards + start screen + save/load
8. ✅ Windows installer packaging

Each step above is engine-complete (tested, playable end to end) with
data-driven content behind it — see `docs/CONTENT_PLAN.md` for exactly
what's shipped and what's still open (more levels, ability execution,
multi-level NPCs).

## License / provenance

This is an original game inspired by, but not a copy of, the proprietary
*Mutants!* door game (Majorware/Majorsoft). No original source code or
copyrighted assets from that game are used here — see the research doc for
what's independently documented vs. newly designed.
