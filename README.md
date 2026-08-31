# ChronTravelers

A standalone, text-based Windows RPG. Its mechanical skeleton is inherited
from the classic Major BBS door game **Mutants!**; the setting is an
original sci-fi / time-travel reskin. Single-player for v1, with all other
"players" in the world simulated as full NPC Travelers running the same
rules, classes, and restrictions as the human player.

You are a **Chron Traveler** — crew from Project Meridian, a classified
government temporal-tunnel program (in the spirit of the old *Time Tunnel*
TV show). On its first full-power run the tunnel tore a standing rupture
that "frayed" the downstream timeline and swept the gantry crew loose.
You surface at some year between 2000 and 5000 A.D. with no way to steer
and no way home. Ride the Ion surges, go as deep downstream as you can,
and level up — the surface team is still looking.

## Status

All 8 milestones of the planned sequence (see Roadmap below) are built:
a playable console game with combat, loot, an NPC economy, time travel,
save/load with leaderboards, and a Windows installer build/release
pipeline.

The world is a **continuous 2000–5000 A.D. timeline** (not discrete
levels): you `travel` to any year for an Ion cost proportional to the
distance, and monsters, loot, maps, and stores all scale smoothly by
year. Each year's map is generated deterministically from a per-save
world seed. "Warden" years — placed by the seed every random 50–100
years — station an automated temporal-defense construct guarding a
Legendary trophy from a pre-collapse tech cache, but gate nothing. In the
year you're standing in, monsters occupy rooms, roam between them, fight
each other (loot drops on the floor — `take` it), and heal from their own
Ion pool. **Ranged weapons** (wands, bows, later guns) sit in their own
equip slot: `wield` one, then `point`/`shoot <dir>` to hit a monster in
the next room. Each has a finite built-in magazine that persists in the
save; once spent it only converts or sells, at a reduced value. Content
is a set of tier-free catalogs in `src/ChronTravelers.Content/*.json`
(`monster-species`, `item-archetypes`, `eras`, `store-templates`), loaded
by `ChronTravelers.Engine.Content.ContentLoader.LoadTimeWorld` into a
`ChronTravelers.Core.Time.TimeWorld`; see `docs/CONTENT_PLAN.md` for the
shape and what's still open (tuning and polish only).

## Building & running

```
dotnet build ChronTravelers.sln
dotnet test ChronTravelers.sln
dotnet run --project src/ChronTravelers.Console
```

To produce the distributable Windows build locally (requires
[Inno Setup 6](https://jrsoftware.org/isinfo.php)):

```
dotnet publish src/ChronTravelers.Console -c Release -r win-x64 -o publish/win-x64
iscc installer/ChronTravelers.iss
```

The installer lands in `installer/Output/`. Pushing a `v*` tag (e.g.
`git tag v0.1.0 && git push origin v0.1.0`) runs the same steps in CI and
attaches the installer to a GitHub Release automatically — see
[`.github/workflows/release.yml`](.github/workflows/release.yml).

Saves and the leaderboard DB live under `%APPDATA%\ChronTravelers\`. If the
game hits an unhandled exception it writes a full report to
`%APPDATA%\ChronTravelers\crashes\crash-<timestamp>.log` (and prints the
path) before exiting — attach that when reporting a crash.

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
- [`src/ChronTravelers.Console/Program.cs`](src/ChronTravelers.Console/Program.cs) — the
  playable console app; its file header notes exactly what's real vs.
  still simplified at any given point.
- [`docs/CONTENT_PLAN.md`](docs/CONTENT_PLAN.md) — what's actually in
  `src/ChronTravelers.Content/*.json` today, and what's still open.

## Roadmap (see `docs/TECH_STACK.md` for detail)

1. ✅ Core domain model (classes, stats, Ions, items) + unit tests
2. ✅ Grid movement
3. ✅ Combat, loot drops, convert/sell/wield (+ spatial monsters, ranged weapons)
4. ✅ NPC simulation loop
5. ✅ Stores (supply depots + player-owned) and the Credit economy
6. ✅ Time travel with scaling — reworked from 8 discrete levels into the
   continuous 2000–5000 A.D. timeline described above
7. ✅ Leaderboards + start screen + save/load
8. ✅ Windows installer packaging

Each step above is engine-complete (tested, playable end to end) with
data-driven content behind it — see `docs/CONTENT_PLAN.md` for exactly
what's shipped and what's still open (tuning and polish only — Ion-cost
balance, persisting player-store ownership, finer era bands).

## License / provenance

This is an original game inspired by, but not a copy of, the proprietary
*Mutants!* door game (Majorware/Majorsoft). No original source code or
copyrighted assets from that game are used here — see the research doc for
what's independently documented vs. newly designed.
