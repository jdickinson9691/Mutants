# Project Agents & Agent Contracts

This project is large enough to split into distinct workstreams. Whether
"agent" means a Claude subagent, a human contributor, or a future AI teammate,
each role below has a fixed contract: what it owns, what it must produce,
what it may **not** decide unilaterally, and who/what it hands off to. Treat
this file as the source of truth for division of responsibility — update it
whenever a role's scope changes.

---

## 1. Project Planning Agent

**Owns:** the roadmap, milestone sequencing, and this repo's issue/task
backlog.

**Inputs:** `docs/GDD.md`, `docs/TECH_STACK.md`, stakeholder (JD) priorities.

**Outputs:**
- A milestone plan matching `docs/TECH_STACK.md` §"Minimum viable milestone
  sequencing," broken into GitHub issues with acceptance criteria.
- Sprint/session summaries when work resumes across sessions (since each new
  Claude session starts cold — see `docs/CONTINUITY.md`).

**Must not:** change game design (defers to Design Agent) or approve
architecture changes unilaterally (defers to Systems Agent).

**Hands off to:** every other agent, via GitHub issues.

---

## 2. Game Design Agent

**Owns:** `docs/GDD.md` and `docs/CONTENT_PLAN.md` — the single source of
truth for mechanics, numbers, and content (classes, abilities, monsters,
loot tables, level themes, economy tuning).

**Inputs:** `research/ORIGINAL_MUTANTS_RESEARCH.md` (what's historically
confirmed vs. not), playtest feedback once a build exists.

**Outputs:**
- GDD updates, always tagged `[SOURCE]` vs. original design per the
  convention already established in `docs/GDD.md`.
- Balance-tuning proposals (numbers), handed to the Systems Agent as data
  file changes, never as direct code edits.

**Must not:** implement code directly. Design changes land in JSON content
files (`src/ChronoTravelers.Content/`) reviewed by the Systems Agent, or as GDD prose
for anything that isn't yet data-driven.

**Hands off to:** Systems/Engine Agent (implementation), Documentation Agent
(player-facing docs).

---

## 3. Systems / Engine Agent

**Owns:** `src/ChronoTravelers.Core/` and `src/ChronoTravelers.Engine/` — the domain model,
combat resolution, Tachyon economy, NPC AI, tick loop, persistence layer.

**Inputs:** `docs/GDD.md`, `docs/TECH_STACK.md`, issues from Project
Planning.

**Outputs:** working, tested C# code; unit tests in `tests/` for every
system (combat math, leveling curve, Tachyon drain/conversion, store pricing,
NPC decision loop) before it's considered done.

**Must not:** invent new mechanics not in the GDD without flagging the
Design Agent first; must not change the public save-file schema without a
migration path.

**Hands off to:** Console/UI Agent (consumes the engine's public API), QA
Agent (verification).

---

## 4. Console / UI Agent

**Owns:** `src/ChronoTravelers.Console/` — the Spectre.Console front end: rendering,
input parsing/commands, the start screen (including the leaderboard display
requirement), status panel.

**Inputs:** the Engine Agent's public API/events; `docs/GDD.md` §10 (UI/
presentation) and the surviving screenshot's visual conventions in
`research/ORIGINAL_MUTANTS_RESEARCH.md` §6.

**Outputs:** the actual playable console app.

**Must not:** embed game logic in UI code — all rules live in Core/Engine so
they stay unit-testable without a console attached.

**Hands off to:** Packaging Agent, QA Agent.

---

## 5. Content Agent

**Owns:** the actual JSON content in `src/ChronoTravelers.Content/` (specific
monster stats, item definitions, level layouts/room text, store inventories)
— i.e., turning the Design Agent's numbers/tables into loadable data files.

**Inputs:** `docs/CONTENT_PLAN.md`.

**Outputs:** validated JSON content files (schema-checked in CI).

**Hands off to:** Systems/Engine Agent (consumes the data), QA Agent.

---

## 6. QA / Verification Agent

**Owns:** test coverage strategy, playtesting checklists, and this project's
definition of "done."

**Inputs:** every other agent's output.

**Outputs:**
- Passing CI (`dotnet test`) required before merge.
- A manual playtest checklist per milestone (movement, combat, economy,
  time travel, leaderboard display, NPC behavior sanity).
- Bug reports filed as GitHub issues, tagged by owning agent.

**Must not:** merge its own fixes without another agent's review for
anything touching game balance or save compatibility.

**Hands off to:** the relevant owning agent for any fix.

---

## 7. Packaging / Release Agent

**Owns:** `installer/` (Inno Setup script) and `.github/workflows/` CI/CD.

**Inputs:** a passing build from the Console/UI Agent.

**Outputs:** a signed-or-unsigned (per project decision) Windows installer
as a GitHub Release asset, produced automatically on tag push.

**Must not:** change game code to "make packaging easier" — packaging
adapts to the app, not the reverse.

**Hands off to:** Project Planning Agent (release notes/announcement).

---

## 8. Documentation Agent

**Owns:** this file, `README.md`, `docs/CONTENT_PLAN.md` structure, and any
player-facing help text / in-game `help` command content.

**Inputs:** all other agents' outputs.

**Outputs:** docs that stay in sync with actual shipped behavior — flagged
as a CI check item (docs reviewed alongside code review, not after).

---

## Cross-cutting rules for every agent

1. **`[SOURCE]` discipline**: any claim about "how the original game worked"
   must cite `research/ORIGINAL_MUTANTS_RESEARCH.md`; anything else is
   labeled original design. Never blur the two.
2. **Data over code** for tunable numbers: if a change is "make goblins hit
   harder" or "raise the level-5 store cost," it's a content-file change,
   not an engine-code change.
3. **No agent merges its own game-balance change without the Design Agent's
   sign-off**, and no agent merges its own engine change without the QA
   Agent's sign-off (self-review is fine for pure refactors/docs).
4. **Every session starts cold.** Because this work spans many Claude
   sessions, each agent's first action on resuming should be reading this
   file plus the relevant doc(s) above, not assuming prior context survived.
