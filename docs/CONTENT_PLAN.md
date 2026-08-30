# Content Plan

The data schema is finalized: `src/Mutants.Content/*.json`, loaded by
`Mutants.Engine.Content.ContentLoader` into the same domain types
(`Item`, `Monster`, `LevelMap`, `Store`, `GameWorld`) everything else in
the engine already works with. Real content now lives directly in that
format — see the files listed below rather than duplicating their content
here. `Mutants.Core.World.TestLevel` / `Monsters.TestMonsters` /
`Economy.TestStores` / `Levels.TestWorld` remain as a small hand-coded
fallback (used automatically if the JSON is missing or malformed) and as
lightweight fixtures for unit tests that don't want file I/O — they are
not launch content.

## Sections

- [x] Monster roster per time-travel level (stats, XP, loot table, tags e.g. "undead")
      — `monsters.json`: 3 regular monsters + 1 gatekeeper per level, tiers 1–5.
      A couple per level are tagged `"undead"` (matches docs/GDD.md §4.2's
      Priest "Turn Undead" ability, which now checks this tag in combat).
- [x] Item catalog per tier/rarity (weapons, armor, consumables, "junk"/convertible items)
      — `items.json`: ~34 items across tiers 1–5, including a few
      class-restricted pieces and a guaranteed-rare "trophy" per gatekeeper.
- [x] Level themes, names, and room-text banks
      — `levels/level-1.json` .. `level-5.json`. 5 levels shipped (the low
      end of the GDD's "5–8 levels for v1 launch" range); levels 6–8 are
      still open — same format, straightforward to add more.
- [x] Store catalog templates per level (what a government store stocks/pays by default)
      — `stores.json`: a government store on levels 1–2 and 4–5 (level 3
      has none yet), plus purchasable empty slots on levels 1, 2, and 4 for
      player ownership.
- [x] NPC population parameters per level (count, starting character-level range)
      — `npc-population.json`: an entry for every level, all now consumed.
      Every level gets its own native NPC population (already unlocked
      through its home level, fast-leveled into its `minLevel`–`maxLevel`
      range and topped off to full HP/Ions), each acting against its own
      current level's map/roster/stores every tick and able to
      independently push one level deeper on its own (see
      `Mutants.Engine.Npc.NpcController`'s Travel goal). Character class
      per NPC is still uniform-random, not config-driven.
- [x] Full ability tables for Warrior, Thief, Priest, Mage, Wizard (tiers 1–6 each)
      — `abilities.json`: Warrior and Priest are docs/GDD.md §4.2-sourced;
      Thief, Mage, and Wizard are original design (each entry's `source`
      field says which) filling the gap the GDD explicitly left open,
      following its own stated pattern (6 tiers, single-target → group/
      area → capstone) and each class's flavor from the GDD's class table.
      **Wired and executable**: `abilities.json` now carries mechanical
      fields (`effect`, `magnitude`, `ionCost`, `condition`, `tag`,
      `durationRounds`) consumed by `Mutants.Engine.Combat.CombatSession`
      — the player's own fights are interactive (`fight` → each round
      `attack` or `cast <ability>`; `abilities` lists what's unlocked).
      Every multi-target/ally GDD ability was adapted to a single-target
      equivalent for this 1v1 engine (see `ContentDtos.AbilityData`'s doc
      comment for the full mapping); 4 abilities with no honest 1v1
      translation (Resurrect Lite, Fence's Favor, Blink, Mana Well) are
      `effect: "None"` and refused at cast time with no Ion cost rather
      than silently doing nothing.

## Open follow-up work (not content — engine features content is now blocked on or ready for)

- Levels 6–8, more monsters/items per level, and a second store per level
  3 — the content pipeline supports all of this already; it's pure volume,
  not new plumbing.
