# Content Plan

The world is a **continuous 2000–5000 A.D. timeline** (docs/GDD.md §3.2),
not a fixed set of levels. Content is a set of **tier-free catalogs** in
`src/Mutants.Content/*.json`, loaded by
`Mutants.Engine.Content.ContentLoader.LoadTimeWorld(dir, worldSeed)` into a
`Mutants.Core.Time.TimeWorld`. `TimeWorld.GetYear(year)` builds a
`YearContent` on demand — map, era, monster roster, store slots,
Gatekeeper — scaling every number from the year via
`Mutants.Core.Time.TimeScale` / `MonsterScaling` / `LootScaling`.
`Mutants.Core.Time.TestTimeWorld` is a 3-era hand-built fallback (used if
the JSON is missing/malformed) and a fixture for tests that don't want
file I/O.

## Catalogs

- [x] **Monster species** — `monster-species.json`. ~25 species: `{ id,
      name, tags, archetype, lootThemeTags }`, no numbers. `archetype` is
      one of `Baseline | Caster | Bruiser | Skirmisher`
      (`Mutants.Core.Time.MonsterArchetype`); `TimelineContentFactory`
      turns it into concrete stats (incl. an Ion pool from
      `MonsterScaling.BaseIons`) at the encounter year, as a fixed offset
      from `MonsterScaling`'s baseline for that year. `"undead"` tags
      carry through to combat (Priest "Turn Undead", GDD §4.2). Loot is
      rolled from item archetypes whose `themeTags` intersect the
      species' `lootThemeTags` (or the era's), scaled to the year.
      **Placed spatially** (GDD §7.1): `YearPopulation.Seed` drops
      `max(2, roomCount/3)` of the year's roster into its rooms on first
      entry, and `MonsterController` roams / infights / heals them each
      tick. No per-year placement content — it's all derived from the
      species roster + the world seed.

- [x] **Item archetypes** — `item-archetypes.json`. ~32 archetypes: `{ id,
      name, type, rarity, restrictedClass?, effect?, effectMagnitude?,
      effectDurationTicks?, themeTags }`, no tier. `TimelineContentFactory.
      ForArchetype(archetype, year)` produces a concrete `Item` whose
      Value / AttackBonus / DefenseBonus come from `LootScaling` at that
      year. Includes a generic staple set (weapon / armour / junk / a
      Heal food / a BuffAttack potion / a BuffDefense potion), one
      class-restricted weapon per class, and per-theme flavour armour and
      junk for eight themes (scrap, neon, ash, drowned, deep, frost,
      orbital, paradox).

- [x] **Era bands** — `eras.json`. 14 bands from year 2000 to 4950,
      `fromYear` ascending (the first must be 2000). Each: `{ fromYear,
      name, roomText[], speciesIds[], itemThemeTags[] }`. A year resolves
      to the last band whose `fromYear` ≤ it (`EraTable.EraForYear`).
      `YearMapFactory` generates that year's grid deterministically from
      `(worldSeed, year)` — a 9–25-room connected blob via
      `GridLevelBuilder` — with room text drawn from the band's pool.
      Bands: Ruined City, The Rust Quarter, The Neon Undercity, The
      Drowned Sprawl, The Ashfall Wastes, The Undercroft, The Buried
      Reaches, The Frostbound Vaults, The Glacier Deeps, The Shattered
      Orbital, The Vacuum Reaches, The Chronofracture, The Long Now, The
      Final Instant.

- [x] **Store template** — `store-templates.json`: `{ playerSlotBaseCost,
      playerSlotCostPerTier }`. Every year gets one government store
      (seeded room; stocks the staple kinds pulled from the year's era
      themes, priced via `EconomyPricing`) and one vacant player slot
      (seeded room; cost `base + perTier·(tier-1)`).

- [x] **Gatekeepers** — no file; `GatekeeperSchedule` places one every
      random 50–100 years from the world seed. A Gatekeeper year's
      `YearContent.Gatekeeper` is a ~3×-HP bullet sponge with a
      guaranteed year-scaled Legendary weapon trophy
      (`Warden of <year>'s <noun>`). Gates nothing (GDD §3.2).

- [x] **NPC count** — `npc-population.json`: `{ "totalCount": N }`.
      `NpcPopulation.Spawn` scatters that many NPCs across the timeline,
      each in a random year, fast-levelled into that year's soft-cap band.
      Character class per NPC is still uniform-random, not config-driven.

- [x] **Ability tables** — `abilities.json` (unchanged by the timeline
      rework). Warrior/Priest are docs/GDD.md §4.2-sourced; Thief/Mage/
      Wizard are original design (`source` field per entry). Mechanical
      fields (`effect`, `magnitude`, `ionCost`, `condition`, `tag`,
      `durationRounds`) drive `Mutants.Engine.Combat.CombatSession`. Four
      abilities with no honest 1v1 translation (Resurrect Lite, Fence's
      Favor, Blink, Mana Well) are `effect: "None"` and refused at cast
      time.

## Validation

`Mutants.Engine.Tests.Content.TimeWorldContentTests` loads the shipped
catalogs and checks: every era/species/theme cross-reference resolves;
sampled years across 2000–5000 generate well-formed, fully-connected maps;
monster/loot power rises with the year; Gatekeeper years are 50–100 years
apart and each yields a Legendary weapon trophy; every year's government
store stocks all staple kinds. `EraTable` / `TimeWorld` constructor
validation is surfaced as `ContentException` by the loader.

## Open follow-up work

Not new plumbing — tuning and polish:

- **Travel throughput** (`IonEconomy.IonsPerYearTravelled = 0.1`): tuned
  after playtesting alongside passive Ion regen + a 3:1 heal ratio +
  starter rations (the early game was an attrition spiral before). Long
  `travel <year>` jumps are still gated by the Ion-pool cap into the
  mid-game — see docs/GDD.md §12.
- **More / finer era bands** for tighter thematic progression.
- **Config-driven NPC class distribution** instead of uniform-random.
- **Denser rosters / catalogs** if the game wants more variety per year.
