# Content Plan

The world is a **continuous 2000–5000 A.D. timeline** (docs/GDD.md §3.2),
not a fixed set of levels. Content is a set of **tier-free catalogs** in
`src/ChronoTravelers.Content/*.json`, loaded by
`ChronoTravelers.Engine.Content.ContentLoader.LoadTimeWorld(dir, worldSeed)` into a
`ChronoTravelers.Core.Time.TimeWorld`. `TimeWorld.GetYear(year)` builds a
`YearContent` on demand — map, era, monster roster, store slots,
Warden — scaling every number from the year via
`ChronoTravelers.Core.Time.TimeScale` / `MonsterScaling` / `LootScaling`.
`ChronoTravelers.Core.Time.TestTimeWorld` is a 3-era hand-built fallback (used if
the JSON is missing/malformed) and a fixture for tests that don't want
file I/O.

## Catalogs

- [x] **Monster generations & species** — `monster-generations.json`. Six
      **500-year monster generations** (2000/2500/3000/3500/4000/4500),
      each a wholly separate ~5-species roster — new names, not reused
      across generations — nested under `{ fromYear, name, species: [...] }`.
      Each species: `{ id, name, tags, archetype, lootThemeTags,
      powerProfile?, behaviorProfile? }`, no tier numbers. `archetype` is
      one of `Baseline | Caster | Bruiser | Skirmisher`
      (`ChronoTravelers.Core.Time.MonsterArchetype`); `TimelineContentFactory`
      turns it into concrete stats (incl. a Tachyon pool from
      `MonsterScaling.BaseTachyons`) at the encounter year, as a fixed
      archetype offset from `MonsterScaling`'s baseline for that year, then
      `powerProfile`'s per-stat multipliers on top (`ChronoTravelers.Core.
      Time.PowerProfile` — hp/attack/defense/speed, default 1.0 each) so
      two species sharing an archetype aren't numerically identical.
      `behaviorProfile` (`ChronoTravelers.Core.Time.BehaviorProfile`) layers
      real per-species behavior onto `MonsterController`'s shared tick loop:
      `fleeBelowHpFraction` (breaks and runs once hurt enough — omitted
      falls back to an archetype default, 0.25 except Bruisers at 0),
      `packHunting` (aggro splashes to same-species roommates),
      `neverInfights`, `aggroRangeBonus`, and `ambushDamageMultiplier`.
      `"echo"` tags carry through to combat (Doctor "Purge Echo", GDD §4.2).
      Loot is rolled from item archetypes whose `themeTags` intersect the
      species' `lootThemeTags` (or the era's — see below), scaled to the
      year. **Which monster generation a year falls in is a separate axis
      from which era it falls in** (`GenerationTable.GenerationForYear`,
      independent of `EraTable.EraForYear`): eras (~200–250 years each) own
      room text and loot/item theming only; generations (fixed 500 years
      each) own the monster roster, stats, and behavior. The two bandings
      deliberately don't line up.
      **Placed spatially** (GDD §7.1): `YearPopulation.Seed` drops
      `max(2, roomCount/3)` of the year's roster into its rooms on first
      entry — plus, in ~half of years, one or two **apex** monsters
      (`TimelineContentFactory.ApexForSpecies`, `Monster.IsApex`, "Frayed
      &lt;species&gt;": ~2.4× HP, ~3.5× XP, gear-heavy loot, near-zero
      aggro, and never flees/pursues regardless of its species'
      `behaviorProfile`) — and `MonsterController` drifts (slow + random) /
      infights / heals them each tick. No per-year placement content — it's
      all derived from the generation's roster + the world seed.

- [x] **Item archetypes** — `item-archetypes.json`. ~95 archetypes: `{ id,
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
    no separate ammo item) and an optional `rangedEffect` (`Weaken` or
    `Stagger`). `EffectMagnitude` doubles as the damage multiplier / effect
    amount. **Every era theme now has its own 2–4-entry ranged ladder**
    (`scrap`: Pipe Slugger + the shared Tension Bow; `neon`: Signal Lance
    (Wand, Weaken) + Riot Taser (Gun, Stagger); `ash`: Flare Cannon;
    `drowned`: Harpoon Launcher + Depth Charge Emitter (Stagger); `deep`:
    Arc-Lantern Wand (Weaken) + Vault-Piercer Rifle; `frost`: Rime-Fletched
    Bow + Cryo Carbine (Stagger); `orbital`: Tesla Arc Rifle, Micro-Thrust
    Harpoon, Ion Caster (Weaken), plus the shared Slug Carbine; `paradox`:
    Causal Disruptor (Stagger), Grandfather's Railgun (Weaken), plus the
    shared Slug Carbine) — replacing the old three-sample-archetype spread.
  - **Consumables are no longer `common`-only.** Beyond the original
    Heal/BuffAttack/BuffDefense trio, `ConsumableEffectType` (see
    `ChronoTravelers.Core.Items`) now also has `BuffSpeed` (a timed Speed/
    turn-order buff), `RestoreTachyons` (an instant flat Tachyon refill —
    the Tachyon-pool counterpart to Heal), and `HealOverTime` (heals every
    tick for a duration, the timed counterpart to Heal) — see
    `Traveler.Consume` / `Traveler.AdvanceEffectTicks`. Every non-`common`
    era theme now authors 2–3 themed consumables mixing old and new effect
    types (e.g. `neon`'s Capacitor Cell is `RestoreTachyons`, `ash`'s
    Smoldering Broth is `HealOverTime`, `scrap`'s Salvaged Stim-Legs is
    `BuffSpeed`), escalating in rarity/magnitude with the theme.

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
      playerSlotCostPerTier, playerSlotCount }`. Every year gets one supply
      store / depot (seeded room; stocks the staple kinds pulled from the
      year's era themes, priced via `EconomyPricing`; exempt from
      maintenance — GDD §6.2) plus up to `playerSlotCount` vacant player
      slots (seeded distinct rooms; cost `base + perTier·(tier-1)` each — a
      map with too few rooms just yields fewer). All three fields are
      optional — a missing file, or a missing `playerSlotCount`, falls back
      to `StoreTemplateData`'s defaults (`100`/`110`/`3`).

- [x] **Wardens** — no file; `WardenSchedule` places one every
      random 50–100 years from the world seed. A Warden year's
      `YearContent.Warden` is a ~3×-HP bullet sponge with a
      guaranteed year-scaled Legendary weapon trophy
      (`Warden of <year>'s <noun>`). Gates nothing (GDD §3.2).

- [x] **NPC count & class distribution** — `npc-population.json`: `{
      "totalCount": N, "classWeights"? }`. `NpcPopulation.Spawn` scatters
      that many NPCs across the timeline, each in a random year,
      fast-levelled into that year's soft-cap band. `classWeights` is an
      optional map of `CharacterClass` name → weight (e.g. `{ "Soldier":
      2, "Doctor": 1 }` spawns twice as many Soldiers as Doctors); a class
      omitted from the map never spawns. Additive — a missing/empty map
      falls back to the original uniform-random pick across every class
      (`ContentLoader.LoadNpcClassWeights`, `NpcPopulation.PickClass`).
      The same weights are threaded into `WorldSimulation`'s respawns
      (`RespawnNear`/`RespawnScattered`) so a replaced NPC keeps drawing
      from the same distribution the initial population did.

- [x] **Ability tables** — `abilities.json` (unchanged by the timeline
      rework). Soldier/Doctor are docs/GDD.md §4.2-sourced; Spy/Scientist/
      Engineer are original design (`source` field per entry). Mechanical
      fields (`effect`, `magnitude`, `tachyonCost`, `condition`, `tag`,
      `durationRounds`) drive `ChronoTravelers.Engine.Combat.CombatSession`.
      Three abilities with no honest 1v1 translation (Crash Cart, Black
      Market Contacts, Jump Rig) are `effect: "None"` and refused at cast
      time.

## Validation

`ChronoTravelers.Engine.Tests.Content.TimeWorldContentTests` loads the shipped
catalogs and checks: every era/species/theme cross-reference resolves;
sampled years across 2000–5000 generate well-formed, fully-connected maps;
monster/loot power rises with the year; Warden years are 50–100 years
apart and each yields a Legendary weapon trophy; every year's government
store stocks all staple kinds. `EraTable` / `TimeWorld` constructor
validation is surfaced as `ContentException` by the loader.

## Open follow-up work

Not new plumbing — tuning and polish:

- **Travel throughput** (`TachyonEconomy.TachyonsPerYearTravelled = 0.04`, with an
  `TachyonEconomy.MinTravelCost = 8` floor per jump): tuned across playtests
  (0.2 → 0.1 → 0.04) alongside passive Tachyon regen, a 3:1 heal ratio, +1
  TachyonsPerLevel on every class, and a steeper early tier curve — so an
  affordable early hop now lands in a meaningfully harder year. The `8`
  floor stops a near-free decade-creep that farmed every year on the way;
  only a full cross-timeline leap is still an end-game Tachyon commit.
- **More / finer era bands** for tighter thematic progression.
- **Denser rosters / catalogs** if the game wants more variety per year.
