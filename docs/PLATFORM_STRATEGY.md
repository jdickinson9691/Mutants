# Platform Strategy: Standalone Single-Player & Multiplayer Options

Reviewed against the live repo at `D:\Ludinn\Development\Mutants` (now named
**ChronTravelers**) as of 2026-08-31. This reflects real inspection of the
current code, docs, tests, installer script, and CI/release workflows — not
a restatement of the original planning docs.

## 1. Current status

The project has moved well past the original planning phase. All 8
roadmap milestones are marked complete in `README.md`:

1. Core domain model + unit tests
2. Grid movement
3. Combat, loot drops, convert/sell/wield (+ spatial monsters, ranged weapons)
4. NPC simulation loop
5. Stores (supply depots + player-owned) and the Credit economy
6. Time travel — reworked from discrete levels into a **continuous
   2000–5000 A.D. timeline**, scaled and generated deterministically per
   world seed
7. Leaderboards + start screen + save/load
8. Windows installer packaging

Architecture confirmed by reading the code directly:

- **`ChronTravelers.Core`** — pure domain model (Characters, Classes,
  Economy, Events, Ions, Items, Monsters, Stats, Time, World). No I/O, no
  console dependency, no persistence dependency. This is exactly the
  separation `docs/TECH_STACK.md` called for.
- **`ChronTravelers.Engine`** — Combat resolution, NPC/monster AI
  (`NpcController`, `MonsterController`), `WorldSimulation` (the tick
  loop), `Persistence` (LiteDB via `GameRepository`), Content loading.
  `WorldSimulation.Tick` takes explicit `Traveler`/NPC lists and a random
  source as parameters — it has no idea a console exists.
- **`ChronTravelers.Console`** — the only layer that touches Spectre.Console
  and stdin/stdout (`Program.cs`, ~76KB, self-documenting file header).
- **`ChronTravelers.Content`** — data-driven JSON catalogs (monster
  species, item archetypes, era bands, store templates, abilities),
  loaded through `ContentLoader` into a `TimeWorld`.
- Test coverage is extensive: dedicated test projects for both Core and
  Engine, with per-system test files (combat, economy, ions, items,
  monsters, NPC behavior, persistence, time/world generation, content
  validation).
- CI (`.github/workflows/ci.yml`) builds and runs the full test suite on
  every push/PR to `main` via `windows-latest`. Release (`release.yml`)
  packages the Inno Setup installer and attaches it to a GitHub Release on
  a `v*` tag push.

**What I could not verify from here:** I don't have a .NET SDK reachable
from this session's cloud sandbox (outbound network to `dot.net` is
blocked), and I have no shell access to your computer — only file
read/write through the device bridge. So I read the code and configuration
directly, but I did not execute `dotnet build`/`dotnet test` myself. Treat
a green run of `ci.yml` on GitHub (or running `dotnet test
ChronTravelers.sln` yourself) as the real confirmation that everything
still compiles and passes.

Per `docs/CONTENT_PLAN.md`, everything still open is tuning/polish (Ion
travel-cost balance, finer era bands, config-driven NPC class
distribution, a fuller ranged-weapon spread) — not missing plumbing.

## 2. Best solution: standalone single-player Windows game

**Verdict: keep what's built.** C#/.NET 8, self-contained single-file
publish, Spectre.Console, LiteDB, Inno Setup, GitHub Actions — this is
already the right architecture for the stated goal, it's fully
implemented, and changing it now would cost real time for no benefit. The
original tech-stack rationale (`docs/TECH_STACK.md`) holds up: a native
.NET publish gives a fast-starting, single-EXE Windows app; Inno Setup
gives a real installer without needing an installed runtime on the
player's machine; LiteDB is a correct fit for a single local save file
plus leaderboard history.

Pre-release punch list, in priority order:

1. **Confirm green CI** on the current `main` (or run `dotnet test
   ChronTravelers.sln` locally) — the one thing I couldn't verify myself.
2. **End-to-end playtest pass** using the QA checklist implied by
   `docs/AGENTS.md`'s QA/Verification Agent: movement, combat, economy,
   time travel, leaderboard display, NPC behavior sanity, save/load
   round-trip.
3. **Code signing (optional but worth deciding now, not later).** An
   unsigned Inno Setup installer will trigger Windows SmartScreen's
   "unrecognized app" warning on first run. For personal/hobbyist
   distribution that's a non-issue (users click "More info → Run
   anyway"); if you want wider, less technical distribution later, an OV
   code-signing certificate (roughly $100–400/yr from a CA) removes the
   warning. Doesn't need deciding today, but it's cheaper to plan for
   before a public release than to retrofit.
4. **Save schema versioning.** Confirm `CharacterSaveData` carries (or add)
   a schema-version field before you ship v1 broadly, so a future content
   change doesn't silently corrupt or misread old saves.
5. **Distribution point.** `release.yml` already produces a GitHub Release
   asset on a version tag — that alone is a fine v1 distribution channel.
   itch.io is a natural next step if you want discoverability beyond
   people who already know the repo.

## 3. Multiplayer: options, ranked by effort vs. payoff

"Make it multiplayer" has several genuinely different answers depending on
ambition. Laid out cheapest-to-build first:

### Option A — Cross-save leaderboard sync (the original's own "MutantLink" model)

The original *Mutants!* had exactly this: a companion product
(`MutantLink`) that let separate BBS installs share high-score tables,
without any shared live world. This is the lowest-risk, lowest-effort
version of "multiplayer" and is a near-exact spiritual match:

- A small hosted web API (ASP.NET Core minimal API is the natural choice
  since it's the same language/ecosystem already in use) backed by a
  small database (SQLite is fine at this scale; Postgres if you want
  headroom), on a cheap VPS or a serverless host.
- Each player's client `POST`s personal-best entries — this reuses the
  existing `LeaderboardEntry` shape from `GameRepository` almost
  unchanged — and `GET`s global standings to show on the start screen
  alongside (or instead of) the local-only leaderboard.
- No realtime play, no server-authoritative combat, no accounts beyond
  maybe a display name. Add basic server-side sanity bounds on submitted
  scores (e.g., reject a "year 9000" or "level 500" that the current
  content tables can't produce) so a hacked client can't just write
  fake numbers to the global board.
- This is genuinely low effort relative to the payoff: it delivers the
  single most requested "multiplayer" feature for a game like this
  (bragging rights across a player base) without touching the game's
  single-player architecture at all.

### Option B — Turn-based / asynchronous shared world (MUD-style — matches the genre's own roots)

Medium effort, and — this is the important finding from actually reading
the code — **the architecture is already most of the way there**:

- `WorldSimulation.Tick` already drives every `Traveler` (player and NPCs
  alike) through the same deterministic engine calls with zero console
  coupling. Hosting that loop inside a long-running server process instead
  of inside the console app is a hosting change, not a redesign.
- The natural migration: stand up an ASP.NET Core service that owns one
  shared `TimeWorld` and calls `WorldSimulation.Tick` on a real clock
  (finally realizing the "every ~2 seconds" background tick the code's own
  comments note is still just a v1 approximation tied to player input).
  Real player connections take over some or all of the `Traveler` slots
  currently filled by simulated NPCs; NPCs can stay in the world
  alongside real players exactly as they do today, keeping the world
  populated when few people are online — which is precisely the point of
  having built NPC simulation as thoroughly as this project already has.
- **Protocol**: SignalR (built into ASP.NET Core, WebSocket-based, handles
  reconnects, and is a same-ecosystem addition to the existing solution)
  is the practical choice for a "real" client. A raw Telnet/TCP text
  protocol is worth considering as an *additional*, optional front end —
  it would let people literally `telnet` into the game the way they
  telnetted into a BBS, which is a nice nod to the source material and
  isn't hard to bolt onto the same server once the engine is
  network-hosted.
- **The one real architectural gap**: LiteDB is a single-writer embedded
  file database — correct for one player's local save, wrong for
  concurrent multi-client writes. Moving persistence to a server-owned
  SQLite database (WAL mode is fine for one server process) or Postgres
  (if you want multiple server instances or cloud elasticity later) is
  the one genuine "swap the library" cost in this migration.
- **The other real gap**: there is no account/auth system today — a "save"
  is just a name in a local LiteDB file. Multiplayer needs at minimum a
  username+password (or a simpler persistent per-install token) so a
  character belongs to an account rather than to whoever's PC the save
  file lives on.
- Server-authoritative combat falls out almost for free: `CombatResolver`,
  `TimeTravelResolver`, and friends already live in `Engine` with no
  console dependency — running them server-side instead of client-side
  (which the client-server split forces anyway) closes the obvious
  "hacked client" cheating vector as a side effect of the migration, not
  as extra work.

### Option C — Realtime shared world with live positional presence

Not recommended as a near-term goal. This game's identity — and the
original's, per the research — is an async, tick-paced MUD, not a
realtime action game. Chasing true realtime (sub-second position sync,
client prediction/reconciliation, lag compensation) would mean rewriting
the tick model for a kind of responsiveness the game doesn't actually need
and that nothing in the design calls for. Worth revisiting only if the
creative vision genuinely changes toward something like a realtime arena
mode — not as a natural next step from where this project is.

### Recommended sequencing

Ship the single-player v1 as-is (§2's punch list), then do **Option A**
essentially as a side project — it's low-risk, doesn't touch game code,
and delivers the "compare yourself to other players" feeling people
usually mean when they say "make it multiplayer" for a game like this.
Treat **Option B** as the real target if there's follow-through appetite
for a shared world: the Core/Engine/Console split already in place means
that migration is a genuine "add a server host and swap the persistence
layer" project, not a rewrite — which is the most useful thing this
review found. Skip Option C unless the goals change.
