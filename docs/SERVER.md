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

**Stores** (docs/GDD.md §6, full parity with the console): `stores` (list
this year's slots) · `shop` (browse the one in your room) · `buy <item>` ·
`sell <item>` / `sell all` (dump junk) · `buy-store` (claim a vacant slot)
· `stock <item> <price>` · `withdraw <item>` · `reprice <item> <price>` ·
`deposit <credits>` · `charge <credits>` · `collect` (owner-only verbs
require standing at a store you own; `collect` alone reaches every store
you own across every year the shared world has visited).

Fights **auto-resolve** (no round-by-round input over a line protocol) and
the loot drops on the floor — `take` it. Death snaps you back to 2000 A.D.
at full health. Characters autosave on disconnect and every ~60 s.

## Not done yet

- **Command parity.** Stores/shopping (including player-owned stores) are
  now at parity with the console (see *Commands* above). Still missing:
  interactive `cast`/abilities, ranged `shoot`, a `look`-after-tick
  nicety. The console keeps its own fuller command loop for now;
  consolidating the two onto the Game layer is the follow-up refactor.
- **Persistence at scale.** LiteDB is fine for one server process; Postgres
  is the swap if multiple instances / cloud elasticity are ever needed.
- **Auth hardening.** Passwords are PBKDF2-hashed but there's no rate
  limiting, lockout, or transport encryption (run it behind a VPN / SSH
  tunnel, or add TLS, before exposing it publicly).
