# Tech Stack Recommendation

## Recommendation: **C# / .NET 8 (LTS), console application**

### Why this fits better than the alternatives

| Requirement | How C#/.NET satisfies it |
|---|---|
| Standalone Windows app with an installer | `dotnet publish` produces a **self-contained single-file EXE** (no separate runtime install needed for the player). Package it with a real installer via **Inno Setup** (free, scriptable, industry-standard for indie Windows installers) or **MSIX** if you want Microsoft Store distribution later. |
| Text-based, ANSI-styled console UI | .NET's `Console` API plus a small library like **Spectre.Console** gives colored text, tables, live-updating status panes, and input prompts — enough to reproduce the ANSI look in the surviving screenshot without writing a terminal renderer from scratch. |
| A tick-driven world with many simulated NPCs | .NET's `Task`/`System.Threading.Timer` and async/await model background ticks cleanly; NPC AI loops (simple FSMs per §7 of the GDD) run fine single-threaded for hundreds of NPCs, and can be parallelized later with `Parallel.ForEach` if the population grows. |
| Persistent saves, leaderboards, store inventories | **SQLite** via `Microsoft.Data.Sqlite` or **LiteDB** (pure C#, zero native deps, trivially embeds in a single-file EXE) — either is a perfect fit for a local single-player save file that also has to store leaderboard history. |
| Data-driven content (classes, abilities, monsters, loot, level layouts) | JSON or YAML content files loaded at startup, deserialized with `System.Text.Json`. Keeps class/ability/monster tuning in `docs/CONTENT_PLAN.md`-adjacent data files instead of hardcoded in game logic — easy for a designer (or an "agent") to tune without touching engine code. |
| Testability | `xUnit` for unit tests on game systems (combat math, Ion economy, leveling curves) decoupled from the console I/O layer. |
| CI/CD | GitHub Actions has first-class `dotnet` support; a workflow can build, test, and produce the installer artifact on every push/tag. |
| Long-term maintainability | Strong typing, mature tooling (Visual Studio / Rider / VS Code + C# Dev Kit), huge ecosystem, and it's a skill that transfers directly if this ever grows into a networked multiplayer version later (ASP.NET Core / SignalR reuse the same language and much of the same domain model). |

### Why not the alternatives

- **Python**: fastest to prototype in, but a "standalone Windows installer"
  story is weaker — PyInstaller/cx_Freeze executables are large, slower to
  start, and packaging native deps (e.g. a real SQLite build) is fussier than
  .NET's single-file publish. Reasonable choice only if the team is
  Python-only and speed-of-iteration outweighs polish.
- **Node.js/TypeScript**: good if a web version is a near-term goal (Node
  code shares easily with a browser front end later), but Windows packaging
  (pkg / nexe / Electron) is heavier-weight for a *text* game than the app
  needs, and Electron in particular is a poor fit for something that should
  feel like a lean console app.
- **C/C++**: authentic to the original's "ANSI C," and would be the choice if
  the goal were literal source-level restoration — but it's needlessly slow
  to develop game systems in in 2026, with no corresponding benefit for a new
  game merely *inspired by* the original.

### Recommended libraries

- **Spectre.Console** — colored console UI, tables, prompts, live status panel.
- **Microsoft.Data.Sqlite** or **LiteDB** — persistence (save file, leaderboards, store state).
- **System.Text.Json** — content file loading (classes, abilities, monsters, loot tables, level data).
- **xUnit** + **FluentAssertions** — testing.
- **Inno Setup** — Windows installer build (scripted, checked into `/installer/`).
- **GitHub Actions** (`dotnet` + a Windows runner) — CI: build, test, and (on tag) produce the installer as a release asset.

### Suggested repo layout (see also `docs/AGENTS.md`)

```
ChronTravelers/
  docs/                  GDD, tech stack doc, agent contracts, content plan
  research/              Source-material research
  src/
    ChronTravelers.Core/        Domain model: classes, abilities, items, monsters, levels, economy
    ChronTravelers.Engine/      Tick loop, NPC AI, combat resolution, persistence
    ChronTravelers.Console/     Spectre.Console front end / the actual playable app
    ChronTravelers.Content/     JSON content data (classes.json, monsters.json, items.json, levels/*.json)
  tests/
    ChronTravelers.Core.Tests/
    ChronTravelers.Engine.Tests/
  installer/             Inno Setup script + assets
  .github/workflows/     CI pipeline
```

### Minimum viable milestone sequencing

1. Core domain model + unit tests (classes, stats, Ions, items) — no UI yet.
2. Grid/movement + a single hardcoded level, playable via console.
3. Combat + loot drops + convert/sell/wield.
4. NPC simulation loop (reuses the same domain model).
5. Stores (supply depots + player-owned) + Credit economy.
6. Time travel between multiple levels, scaling.
7. Leaderboards + start-screen display + save/load.
8. Installer packaging + first internal Windows build.
