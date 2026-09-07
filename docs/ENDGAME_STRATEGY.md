# Endgame Strategy — Reaching Year 5000

The hard level cap was raised 30 → 60 (docs/GDD.md §4.1) so a Traveler
keeps growing across the whole 2000–5000 A.D. timeline instead of maxing
out around year 2500 and riding the back 5/6 of the timeline with frozen
power. That raise left five gaps between "a level-60 character exists" and
"year 5000 is a place worth reaching." This document is the checklist of
recommendations that close them; every one is now implemented, and the
code/tests/GDD refer back here by number.

## Recommendation 1 — a second wave of passive traits

`ChronoTravelers.Core.Characters.PassiveTrait` / `PassiveTraits` — always-on,
unlocked automatically by `Level`, no activation/UI/AI decision (docs/GDD.md
§4.2.1). Each class had six (levels 1–28, Engineer 1–19), each roughly
midway between a pair of active-ability unlock levels.

Each class now has **twelve**: a second wave of six across levels 33–58
(Engineer 31–56, on its compressed schedule). Second-wave passives are
smaller top-ups on the **same** `PassiveHook` as a first-wave sibling, so
the two stack via `PassiveTraits.Sum` (e.g. Soldier's level-1 *Hardened*
+20% armor Defense and level-33 *Reinforced Plating* +10% sum to +30%).
Each class's second wave matches its flavor — Soldier leans
defense/ambush/armor, Doctor heal/tachyon, Spy economy/speed/aggro,
Scientist tachyon/caster/crit-economy, Engineer defense/speed/junk/
cast-discount.

One second-wave passive per class introduces a genuinely new hook rather
than topping up an old one — currently only Soldier's level-58 *Anomaly
Killer* (see recommendation 3).

## Recommendation 2 — a 7th ability tier

`abilities.json` gained a 7th tier per class (`source: "endgame"`):
level 35 for Soldier/Doctor/Spy/Scientist, level 25 for Engineer's
compressed cadence. Each is a stronger variant of that class's existing
kit — higher `tachyonCost` and `magnitude` than tier 6 — not a wholly new
mechanic:

| Class | Lv | Ability | Effect |
|---|---|---|---|
| Soldier | 35 | *Executioner's Volley* | even heavier bonus damage vs. a target below 25% HP |
| Doctor | 35 | *Field Sanctuary* | a much larger field heal, past *Broad-Spectrum* |
| Spy | 35 | *Vanishing Act* | slip out of sight, guaranteed armour-ignoring crit next attack |
| Scientist | 35 | *Cataclysm* | past *Rupture* — a seam torn wide enough to swallow the room |
| Engineer | 25 | *Overload Discharge* | one armour-melting, defence-ignoring burst |

`Leveling.AbilityTierCount` is 7 and `Leveling.TopAbilityLevel` is 35 to
match. Levels past the 7th tier (36–60, Engineer 26–60) are still
stat/HP/Tachyon growth only.

## Recommendation 3 — a mechanical hook for the paradox theme

The `"paradox"` era theme (The Chronofracture onward, years 4600+ — see
`eras.json`) was flavour text only. It now has a mechanical hook:
`TimeWorld.Build` checks whether the year's era's `itemThemeTags` includes
`"paradox"` and threads that through `TimelineContentFactory.ForSpecies`
(new `eraHasParadoxTheme` parameter, defaulting false), so **every** monster
spawned in a paradox-themed era also carries a `"paradox"` tag — the same
append pattern as the existing `"caster"` tag.

Soldier's second-wave level-58 passive *Anomaly Killer*
(`PassiveHook.ParadoxDamageBonusPct`, +18% attack damage vs. a
`"paradox"`-tagged monster) reads it, mirroring how Scientist's existing
*Field Calibration* reads `"caster"`. The year-5000 capstone
(recommendation 4) also carries the tag, so the passive is relevant
against the very last fight on the timeline.

## Recommendation 4 — a guaranteed year-5000 capstone

`WardenSchedule` now force-adds `TimeScale.MaxYear` (year 5000) as a Warden
year regardless of where the random 50–100-year walk lands — a deliberate
exception to the gap rule (`WardenScheduleTests`
/ `TimeWorldContentTests` skip year 5000 in the spacing check).

`TimelineContentFactory.Warden(seed, year)` special-cases year 5000 to
`FinalWarden` — **"The Convergence"**, named for The Final Instant's own
room text — instead of the regular tier-scaled formula:

| | Regular Warden | The Convergence |
|---|---|---|
| HP | 3× base | 5× base |
| XP / Credit reward | 5× | 10× |
| Trophy `powerMultiplier` | 2.8 (`WardenTrophyPower`) | 4.0 (`FinalWardenTrophyPower`) — best-in-game |
| Tags | — | `"paradox"`, `"capstone"` |

Console and multiplayer kill/afar-kill messages read `monster.Name` /
`target.Name` rather than a hardcoded `"Warden of {year}"` so the capstone
narrates as "The Convergence," not "The Warden of 5000."

## Recommendation 5 — charge a long jump over several ticks

A jump farther than `Traveler.ChargeTravelThresholdYears` (750 years) still
costs the same Tachyons, spent up front, but no longer arrives immediately.
It "charges" for `Traveler.TicksRequiredForChargedTravel(distance)` world
ticks — 1 at the threshold, +1 per additional 250 years, capped at 10 —
before `WorldSimulation`'s per-tick `Traveler.AdvancePendingTravel()` call
resolves it (`SetCurrentYear` / `PlaceAt` + a `GameEvent.TimeTraveled`
broadcast, the same calls a normal jump makes, just deferred).

- `travel` again toward the **same** target while charging: a no-op status
  query (no re-charge, no double spend).
- toward a **different** target: cancels the old charge — no refund, you
  already committed those Tachyons — and starts the new one.
- `TimeTravelResolver.Travel` returns a `TimeTravelResult` with
  `IsCharging: true` and `ChargingTargetYear` / `ChargingTicksRequired`
  instead of a `NewYear` while a charge is in flight. Every pre-existing
  caller that only checks `Success` / `NewYear` / `FailureReason` keeps
  working.
- The pending charge round-trips through a save
  (`CharacterSaveData.ChargingTargetYear` / `ChargingTicksRequired` /
  `ChargingTicksRemaining`, all nullable — an old save restores to "not
  charging").
- The console prints "Charging a jump to `<year>` A.D. — arrival in `<N>`
  tick(s)."

**NPCs never charge.** They are designed to resolve travel instantly.
`NpcController`'s ordinary hop tops out at `MaxTravelHop` (300, 450 for a
Wanderer); its one long-range path — a full jump toward a rotating
occupied-year anchor — is clamped in `TryTravel` so the hop never crosses
`ChargeTravelThresholdYears` in one tick (a more distant anchor is
approached one big affordable hop at a time on successive ticks). The
shared-world server's own `travel` command
(`ChronoTravelers.Game.Commands.Travel`) resolves inline rather than through
`TimeTravelResolver` and is unaffected — a follow-up if multiplayer wants
the same mechanic.

See docs/GDD.md §12 for how this fits the travel economy.
