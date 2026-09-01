# ChronTravelers shared-world server

`docs/PLATFORM_STRATEGY.md` Option B — one `TimeWorld` ticking on a real
clock that any number of players connect into, with the NPC population
keeping it alive when few humans are online. This is the **foundation**:
a working vertical slice, not feature parity with the single-player
console yet (see *Not done yet* below).

## Layout

| Project | Role |
|---|---|
| `ChronTravelers.Game` | Transport-agnostic shared-world layer — `SharedGame` (owns the world + sessions + `WorldSimulation`, one lock), `Session`, `Commands` (the verb set), `Render` (plain-text output via `IGameOutput`). No networking. |
| `ChronTravelers.Server` | The host: bootstraps a `SharedGame`, runs the tick loop, and serves a **telnet** front end — account login (PBKDF2), character select/create, then a line REPL. LiteDB `server.db` for accounts + characters. |
| `ChronTravelers.Game.Tests` | xUnit coverage for the Game layer. |

`WorldSimulation.TickMultiplayer(IReadOnlyList<PlayerTickState>)` is the
one Engine addition — the N-player counterpart to `Tick(player)`: Ion
bookkeeping once per player *and* NPC, one NPC AI pass, then the spatial
monster sim once per occupied year (anchored on a rotating player so
ambush/narration are shared fairly), and an unattended pass elsewhere.

## Run it

```
dotnet run --project src/ChronTravelers.Server -- [--port N] [--db PATH] [--tick-ms N] [--seed N]
```

Defaults: port `4000` (or `$CHRONTRAVELERS_PORT`), `%APPDATA%\ChronTravelers\server.db`,
2000 ms tick, a fresh random seed each start.

## Connect

```
telnet <host> 4000
```

You'll be asked for an account name (new ones are created on the spot with
a password), then to pick or create a Traveler — a new one is offered only
the classes that account hasn't played. Then you're in the shared world.

### Commands

`look [dir]` · `n`/`s`/`e`/`w` · `monsters` · `status` · `inventory` ·
`heal` · `take [all]` · `fight [name]` · `wield <item>` · `convert <item>` ·
`travel <year | +N | -N>` · `news` · `who` · `say <msg>` · `wait` · `quit`

Fights **auto-resolve** (no round-by-round input over a line protocol) and
the loot drops on the floor — `take` it. Death snaps you back to 2000 A.D.
at full health. Characters autosave on disconnect and every ~60 s.

## Not done yet

- **Rich client.** Only telnet today. SignalR + a `ChronTravelers.Console --connect`
  mode is the next transport (the doc's "real client").
- **Command parity.** No stores/shopping, no interactive `cast`/abilities,
  no player-owned stores, no ranged `shoot`, no `look`-after-tick nicety.
  The console keeps its own fuller command loop for now; consolidating the
  two onto the Game layer is the follow-up refactor.
- **Persistence at scale.** LiteDB is fine for one server process; Postgres
  is the swap if multiple instances / cloud elasticity are ever needed.
- **Auth hardening.** Passwords are PBKDF2-hashed but there's no rate
  limiting, lockout, or transport encryption (run it behind a VPN / SSH
  tunnel, or add TLS, before exposing it publicly).
