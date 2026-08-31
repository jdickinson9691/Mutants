# Content Plan

The world is a **continuous 2000–5000 A.D. timeline** (docs/GDD.md §3.2),
not a fixed set of levels. Content is a set of **tier-free catalogs** in
`src/ChronTravelers.Content/*.json`, loaded by
`ChronTravelers.Engine.Content.ContentLoader.LoadTimeWorld(dir, worldSeed)` into a
`ChronTravelers.Core.Time.TimeWorld`. `TimeWorld.GetYear(year)` builds a
`YearContent` on demand — map, era, monster roster, store slots,
Warden — scaling every number from the year via
`ChronTravelers.Core.Time.TimeScale` / `MonsterScaling` / `LootScaling`.
`ChronTravelers.Core.Time.TestTimeWorld` is a 3-era hand-built fallback (used if
the JSON is missing/malformed) and a fixture for tests that don't want
file I/O.

## Catalogs

- [x] **Monster species** — `monster-species.json`. ~25 species: `{ id,
      name, tags, archetype, lootThemeTags }`, no numbers. `archetype` is
      one of `Baseline | Caster | Bruiser | Skirmisher`
      (`ChronTravelers.Core.Time.MonsterArchetype`); `TimelineContentFactory`
      turns it into concrete stats (incl. an Ion pool from
      `MonsterScaling.BaseIons`) at the encounter year, as a fixed offset
      from `MonsterScaling`'s baseline for that year. `"echo"` tags
      carry through to combat (Doctor "Purge Echo", GDD §4.2). Loot is
      rolled from item archetypes whose `themeTags` intersect the
      species' `lootThemeTags` (or the era's), scaled to the year.
      **Placed spatially** (GDD §7.1): `YearPopulation.Seed` drops
      `max(2, roomCount/3)` of the year's roster into its rooms on first
      entry — plus, in ~half of years, one or two **apex** monsters
      (`TimelineContentFactory.ApexForSpecies`, `Monster.IsApex`, "Frayed
      &lt;species&gt;": ~2.4× HP, ~3.5× XP, gear-heavy loot, near-zero
      aggro) — and `MonsterController` drifts (slow + random) / infights /
      heals them each tick. No per-year placement content — it's all
      derived from the species roster + the world seed.

- [x] **Item archetypes** — `item-archetypes.json`. ~55 archetypes: `{ id,
      name, type, powerMultiplier? | rarity, restrictedClass?, effect?,
      effectMagnitude?, effectDurationTicks?, rangedKind?, ammoCapacity?,
      rangedEffect?, themeTags }`, no tier. `TimelineContentFactory.
      ForArchetype(archetype, year)` produces a concrete `Item` whose
      Value / AttackBonus / DefenseBonus come from `LootScaling` at that
      year.
  - **Equippables (weapon / armour / ranged) author `powerMultiplier`,
    not `rarity`** — ~0.5 crude → 1.0 standard → ~1.8 fine → ~2.9 relic.
    `LootScaling.EquipBonusFor(tier, mult)` scales the per-tier baseline
    (`4.4·tier + 1`) by it; `Rarity.ForPower` derives the rarity band;
    `Rarity.DropWeight` makes rare bands scarce on loot tables. Each era
    theme carries a crude → standard → fine ladder plus an occasional
    master/relic; plus one class-restricted weapon per class (Uncommon).
    Consumables/junk still author `rarity`.
  - **Ranged weapons** (`type: "Ranged"`, GDD §5): a `rangedKind` of
    `Wand`/`Bow`/`Gun` plus an `ammoCapacity` (the built-in shot count —
    no separate ammo item) and an optional `rangedEffect` (`Weaken`).
    `EffectMagnitude` doubles as the damage multiplier / Weaken amount.
    v1 ships three: Hexbolt Wand (`common`, Weaken, 5), Recurve Bow
    (`scrap`/`ash`, 10), Slug Carbine (`orbital`/`paradox`, 8). A full
    per-era spread (slings → longbows → muskets → rifles → railguns) is a
    later content pass.

- [x] **Era bands** — `eras.json`. 14 bands from year 2000 to 4950,
      `fromYear` ascending (the first must be 2000). Each: `{ fromYear,
      name, roomText[], speciesIds[], itemThemeTags[] }`. A year resolves
      to the last band whose `fromYear` ≤ it (`EraTable.EraForYear`).
      `YearMapFactory` generates that year's grid deterministically from
      `(worldSeed, year)` — a 9–25-room connected blob via
      `GridLevelBuilder` — with room text drawn from the band's pool.
      Bands: The Fallout Belt, The Rust Quarter, The Neon Undercity, The
      Drowned Sprawl, The Ashfall Wastes, The Deep Archive, The Buried
      Reaches, The Frostbound Vaults, The Glacier Deeps, The Shattered
      Orbital, The Vacuum Reaches, The Chronofracture, The Long Now, The
      Final Instant.

- [x] **Store template** — `store-templates.json`: `{ playerSlotBaseCost,
      playerSlotCostPerTier }`. Every year gets one supply store / depot
      (seeded room; stocks the staple kinds pulled from the year's era
      themes, priced via `EconomyPricing`) and one vacant player slot
      (seeded room; cost `base + perTier·(tier-1)`).

- [x] **Wardens** — no file; `WardenSchedule` places one every
      random 50–100 years from the world seed. A Warden year's
      `YearContent.Warden` is a ~3×-HP bullet sponge with a
      guaranteed year-scaled Legendary weapon trophy
      (`Warden of <year>'s <noun>`). Gates nothing (GDD §3.2).

- [x] **NPC count** — `npc-population.json`: `{ "totalCount": N }`.
      `NpcPopulation.Spawn` scatters that many NPCs across the timeline,
      each in a random year, fast-levelled into that year's soft-cap band.
      Character class per NPC is still uniform-random, not config-driven.

- [x] **Ability tables** — `abilities.json` (unchanged by the timeline
      rework). Soldier/Doctor are docs/GDD.md §4.2-sourced; Spy/Scientist/
      Engineer are original design (`source` field per entry). Mechanical
      fields (`effect`, `magnitude`, `ionCost`, `condition`, `tag`,
      `durationRounds`) drive `ChronTravelers.Engine.Combat.CombatSession`.
      Three abilities with no honest 1v1 translation (Crash Cart, Black
      Market Contacts, Jump Rig) are `effect: "None"` and refused at cast
      time.

## Validation

`ChronTravelers.Engine.Tests.Content.TimeWorldContentTests` loads the shipped
catalogs and checks: every era/species/theme cross-reference resolves;
sampled years across 2000–5000 generate well-formed, fully-connected maps;
monster/loot power rises with the year; Warden years are 50–100 years
apart and each yields a Legendary weapon trophy; every year's government
store stocks all staple kinds. `EraTable` / `TimeWorld` constructor
validation is surfaced as `ContentException` by the loader.

## Open follow-up work

Not new plumbing — tuning and polish:

- **Travel throughput** (`IonEconomy.IonsPerYearTravelled = 0.04`): tuned
  across playtests (0.2 → 0.1 → 0.04) alongside passive Ion regen, a 3:1
  heal ratio, +1 IonsPerLevel on every class, and a steeper early tier
  curve — so an affordable early hop now lands in a meaningfully harder
  year. Only a full cross-timeline leap is still an end-game Ion commit.
- **More / finer era bands** for tighter thematic progression.
- **Config-driven NPC class distribution** instead of uniform-random.
- **Denser rosters / catalogs** if the game wants more variety per year.
- **A full per-era ranged-weapon spread** (slings → longbows → muskets →
  rifles → railguns, with era-appropriate `rangedEffect`s) — v1 ships only
  three sample archetypes.
