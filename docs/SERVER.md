# Chrono Travelers shared-world server

`docs/PLATFORM_STRATEGY.md` Option B — one `TimeWorld` ticking on a real
clock that any number of players connect into, with the NPC population
keeping it alive when few humans are online. This is the **foundation**:
a working vertical slice, not feature parity with the single-player
console yet (see *Not done yet* below).

## Layout

| Project | Role |
|---|---|
| `ChronoTravelers.Game` | Transport-agnostic shared-world layer — `SharedGame` (owns the world + sessions + `WorldSimulation`, one lock), `Session`, `Commands` (the verb set), `Render` (plain-text output via `IGameOutput`). No networking. |
| `ChronoTravelers.Server` | The host: bootstraps a `SharedGame`, runs the tick loop, and serves **two** front ends onto it — raw **telnet** and a **SignalR hub** (`/game`). Both do account login (PBKDF2), character select/create, then a command stream. LiteDB `server.db` for accounts + characters. |
| `ChronoTravelers.Game.Tests` | xUnit coverage for the Game layer + `CharacterFactory`. |

`ChronoTravelers.Console --connect <url>` is the SignalR client — the
console's normal renderer, but every line comes from the server's
`Receive` push and every command goes out via `Send`.

`WorldSimulation.TickMultiplayer(IReadOnlyList<PlayerTickState>)` is the
one Engine addition — the N-player counterpart to `Tick(player)`: Tachyon
bookkeeping once per player *and* NPC, one NPC AI pass, then the spatial
monster sim once per occupied year (anchored on a rotating player so
ambush/narration are shared fairly), and an unattended pass elsewhere.

## Run it

```
dotnet run --project src/ChronoTravelers.Server -- [--port N] [--http-port N] [--db PATH] [--tick-ms N] [--seed N]
```

Defaults: telnet port `4000` (or `$CHRONOTRAVELERS_PORT`), SignalR/HTTP port
`5000` (or `$CHRONOTRAVELERS_HTTP_PORT`), `%APPDATA%\ChronoTravelers\server.db`,
2000 ms tick, a fresh random seed each start.

## Connect

**SignalR client (the console):**

```
ChronoTravelers.exe --connect http://<host>:5000
```

**Telnet:**

```
telnet <host> 4000
```

Either way: account name (new ones are created on the spot with a
password), then pick or create a Traveler — a new one is offered only the
classes that account hasn't played. Then you're in the shared world.

### Commands

`look [dir]` · `n`/`s`/`e`/`w` · `monsters` · `status` · `inventory` ·
`heal` · `take [all]` · `fight [name]` · `wield <item>` · `convert`/`con` `<item>` ·
`travel <year | +N | -N>` · `news` · `who` · `say <msg>` · `wait` · `quit`

A jump past `Traveler.ChargeTravelThresholdYears` (750 years) charges over
several world ticks instead of arriving instantly — same as the console
(docs/ENDGAME_STRATEGY.md recommendation 5, docs/GDD.md §12): Tachyons are
spent up front, `travel` reports "Charging a jump to `<year>` A.D. —
arrival in `<N>` tick(s)", and `WorldSimulation.TickMultiplayer` completes
the jump (and broadcasts it) a few ticks later. Calling `travel` again
toward the same target while charging is a no-op status query; toward a
different target cancels the old charge (no refund) and starts the new one.

`wield` a wand/bow/gun, then `fight [name]` a monster in your room — with a
ranged weapon readied, `fight` locks the monster in as your ranged target
instead of engaging melee (`draw a bead on...`) rather than resolving a
fight immediately. From there `point <dir>` (wands) or `shoot <dir>` /
`fire <dir>` (bows/guns) fires at the locked target down a straight,
unbroken chain of exits that way, out to the weapon's own range —
kiting: line up, back off, keep firing, same as the console
(docs/GDD.md §5). The lock-on/kite flow above is Monster-only — PvP
(`fight` against an NPC Traveler, below) instead fires one opening shot
per side automatically, before the fight resolves, if that side has a
readied, loaded ranged weapon (docs/GDD.md §11's "Implementation
(2026-09-08)" note) — there's no room to kite across in a single
auto-resolved fight.

`abilities`/`spells` lists your class's ability tree. `cast <name>` works
for exactly two of them — Engineer's `Jump Rig` and Doctor's `Crash Cart`
(docs/GDD.md §4.2's "Implementation (2026-09-07)" note) — any time, not
just mid-fight, and this server's `fight` against a monster still just
auto-resolves basic attacks with no round of your own to cast one in.
PvP (`fight` against an NPC Traveler, below) is the exception: it now
casts abilities on both sides automatically as part of that auto-resolve
(docs/GDD.md §11's "Implementation (2026-09-08)" note) via an AI chooser,
not a prompt — there's still no interactive round for a human on either
side of a PvP bout, same as before.

**Stores** (docs/GDD.md §6, full parity with the console): `stores` (list
this year's slots) · `shop` (browse the one in your room) · `buy <item>` ·
`sell <item>` (no store buys junk) / `sell all` (converts junk for Tachyons, works anywhere) · `repair <item>` (restores a worn weapon/armor's Durability for Credits — §6.3's Credit sink) · `buy-store` (claim a vacant slot)
· `stock <item> <price>` · `withdraw <item>` · `reprice <item> <price>` ·
`deposit <credits>` · `charge <credits>` · `collect` (owner-only verbs
require standing at a store you own; `collect` alone reaches every store
you own across every year the shared world has visited).

Fights **auto-resolve** (no round-by-round input over a line protocol) and
the loot drops on the floor — `take` it. With no monster in your room,
`fight [name]` instead targets a living NPC Traveler sharing your tile
(docs/GDD.md §11's "player-vs-NPC-Traveler combat") — win and their whole
inventory, equipped gear included, drops for you to `take`; lose and it's
the same death & recall as losing to a monster. This PvP fight uses each
side's class abilities (an AI chooser picks a round's cast, same
self-preservation/rationing heuristic the NPC AI uses elsewhere) and one
opening ranged shot per side if readied (docs/GDD.md §11's "Implementation
(2026-09-08)" note) — not the basic-attack-only fight it used to be. Death
(docs/GDD.md §3.3,
shared with the console via `ChronoTravelers.Game.DeathRecall`) drops half your
unequipped inventory where you fell, costs half your current Tachyons, and
snaps you back to 2000 A.D. at full health. Characters autosave on
disconnect and every ~60 s.

## Not done yet

- **Command parity.** Stores/shopping (including player-owned stores),
  ranged combat (`shoot`/`point`/`fire`, see *Commands* above), and PvP
  ability casting/ranged (see PvP paragraph above) are now at parity with
  the console. Still missing: interactive mid-fight ability casting against
  a *monster* (moot here — `fight` already auto-resolves those, so there's
  no round to cast one in) and a `look`-after-tick nicety. The console
  keeps its own fuller command loop for now; consolidating the two onto the
  Game layer is the follow-up refactor.
- **Persistence at scale.** LiteDB is fine for one server process; Postgres
  is the swap if multiple instances / cloud elasticity are ever needed.
- **Auth hardening.** Passwords are PBKDF2-hashed but there's no rate
  limiting, lockout, or transport encryption (run it behind a VPN / SSH
  tunnel, or add TLS, before exposing it publicly).
