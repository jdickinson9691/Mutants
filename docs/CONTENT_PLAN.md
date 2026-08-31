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
      — `monsters.json`: 3 regular monsters + 1 gatekeeper per level, tiers
      1–8, all following `Mutants.Core.Monsters.MonsterScaling`'s baseline
      curve (a tier-N monster is a sensible fight around character level
      `10 * N`; a gatekeeper is ~3x a regular monster's HP at the same
      attack/defense/speed). A couple per level are tagged `"undead"`
      (matches docs/GDD.md §4.2's Priest "Turn Undead" ability, which
      checks this tag in combat).
- [x] Item catalog per tier/rarity (weapons, armor, consumables, "junk"/convertible items)
      — `items.json`: ~60 items across tiers 1–8, including a few
      class-restricted pieces per tier (rotating through all 5 classes)
      and a guaranteed Legendary "trophy" per gatekeeper. Every item is
      convertible/sellable regardless of type, and value (so Ion/Riblet
      payout) always scales by the item's own tier and rarity via
      `Mutants.Core.Items.LootScaling` — there's no type-based
      restriction anywhere in `Mutant.Convert`/`Sell` or `Store`.
      **Consumables are now usable**: every tier has one food item
      (`effect: "Heal"`, flat HP, no duration) and one potion
      (`BuffAttack` or `BuffDefense`, a temporary stat bonus lasting 15
      world ticks) — `use`/`eat`/`drink <item>` in the console, backed by
      `Mutant.Consume`/`AdvanceEffectTicks`. A Consumable with no `effect`
      data is flavor-only (still sellable/convertible, but "use" refuses
      it) — none currently ship that way, but the schema supports it.
- [x] Level themes, names, and room-text banks
      — `levels/level-1.json` .. `level-8.json`. All 8 levels of the GDD's
      "5–8 levels for v1 launch" range now shipped: Ruined City, Neon
      Undercity, Ashfall Wastes, Drowned Archives, The Undercroft, The
      Frostbound Vaults, The Shattered Orbital, and the finale — The
      Chronofracture.
- [x] Store catalog templates per level (what a government store stocks/pays by default)
      — `stores.json`: a government store on every level, plus a
      purchasable empty slot on every level for player ownership (levels
      1, 2, 4 had one already; 3, 6, 7, 8 gained one in this pass — level 5
      is the one level still without a purchasable slot).
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

All GDD-mandated content sections above are now fully populated (all 8
levels, every level's monster/item/store/NPC-population entries). What
remains is pure volume, not new plumbing, whenever there's appetite for
it: more monsters/items per level (each level's roster is still a lean
"3 regular + 1 gatekeeper"), a purchasable store slot for level 5 to match
every other level, and levels beyond 8 if the game ever wants to extend
past the GDD's stated v1 range.
