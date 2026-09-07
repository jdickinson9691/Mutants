# Game Design Document — "Chrono Travelers"

A standalone, single-player-capable, text-based RPG. Its mechanical skeleton
is inherited from the Major BBS door game *Mutants!* (the `[SOURCE]` marks
below); the setting, lore, classes, content, and naming are an original
sci-fi / time-travel reskin. Version 1 simulates all other "players" as NPCs
with the same classes, rules, and restrictions as the human player, so the
world feels populated even with nobody else connected. Multiplayer/network
play is an explicit non-goal for v1 and a likely v2+ direction (see §11).

Everything here is original design **except** where marked `[SOURCE]`, which
means it's a confirmed mechanic from `research/ORIGINAL_MUTANTS_RESEARCH.md`
(the historical record of the door game whose rules this borrows). Anything
not marked that way fills a documented gap in that record or is part of the
Chrono Travelers reskin.

---

## 1. High concept

You are a **Chrono Traveler** — crew from Project Meridian, a classified
government temporal-tunnel program (think the *Time Tunnel* of the old TV
show). On its first full-power run the tunnel tore a **standing rupture**
that "frayed" the downstream timeline; the personnel on the gantry were
swept loose and now surface at random years between **2000 and 5000 A.D.**,
unable to steer. You explore a grid-based city and wasteland, fight the
things the fray left behind for loot, convert salvage into **Tachyons**
(tunnel-charge) to survive and to ride surges through time, buy and run
stores, and burn Tachyons to `travel` anywhere on the 2000–5000 A.D. timeline —
later years are richer and far more dangerous. Every other Traveler you meet
— friendly, hostile, or running a shop — is an NPC governed by the same
rules you are. The goal (and the board): push deepest downstream and level
up. The surface team never stops looking; it just can't pull you back.

### 1.1 Background & lore (Chrono Travelers reskin)

- **Project Meridian** built the tunnel underground, on government money, to
  observe (not touch) the past. The first activation at full power held for
  about eight seconds and never fully closed — leaving a rupture that bleeds
  "downstream," so the further into the future you go the more *frayed*
  reality is (§3.2's era bands run from a barely-touched near future to a
  timeline that has come apart entirely).
- **Chrono Travelers** are the gantry crew, scattered 2000–5000 A.D. They
  move by riding **Tachyon surges** — the tunnel-charge that leaks from the
  rupture — and the cost scales with how far they jump (§2, §3.2).
  (**Tachyons** are this reskin's name for the original game's "ions".)
- **Echoes** (the monster tag, was "undead") are fray-recordings: the
  timeline replaying dead people and events on a loop. The Doctor's
  resonance tools are especially good at collapsing them.
- **Wardens** (was "Gatekeepers") are automated temporal-defense
  constructs the program seeded along the timeline to guard **pre-collapse
  technology caches**. They still stand watch, decades apart, over a
  guaranteed high-end trophy. They gate nothing — you can travel straight
  past one.
- **Credits** (was "Riblets") are post-collapse scrip — the currency every
  surviving settlement and salvage store runs on.

## 2. Core resource: Tachyons `[SOURCE]`

Tachyons are the single unified resource for:
- **Survival** — passive drain per turn/tick; hitting 0 starts costing HP.
- **Healing** — the `heal` command spends Tachyons to heal wounds directly,
  usable at any time (no location or combat requirement) and, like every
  other action, advances one tick `[SOURCE]`.
- **Spellcasting** — arcane/divine classes spend Tachyons per ability.
- **Time travel** — spend Tachyons proportional to how many years you jump
  (see §3.2) to move anywhere on the 2000–5000 A.D. timeline.

Tachyons are generated almost entirely by **converting items** — `convert <item>`
destroys the item and adds Tachyons based on the item's tier/value `[SOURCE
mechanic, original value curve]`. This makes every piece of loot a three-way
choice, mirrored exactly from the source game: **wield it, sell it, or burn
it** `[SOURCE]`. The player's Tachyon pool has **no ceiling** (original change) —
converting loot never overflows or gets wasted, so a stockpile for a long
downstream jump is always worth building. Level-up still raises a *nominal*
pool size that scales a couple of abilities and the passive-regen cap; it's
just not a hard limit on how much you can hold. (NPC and monster pools
stay capped.)

### 2.1 Tachyon economy tuning (original)
- Passive drain: 1 Tachyon per N game-ticks, scaled slightly up further into
  the future (later years are harsher survival environments) — the scaling
  key is the whole-number difficulty tier for the current year (see §3.2).
- Passive **regen**: 1 Tachyon per M game-ticks out of combat, faster than the
  drain in early years and slower in the far future. Net effect: the
  present is survivable (grind → heal → recover), the deep future
  net-drains you. Added after playtesting showed the drain-only model made
  the early game an unrecoverable attrition spiral. Regen alone tops out
  at the nominal pool size (you can't wait your way to an infinite pool);
  only converting loot pushes past it.
- Item→Tachyon conversion value = `base_item_value * rate`, rounded down, with a
  minimum of 1. `rate` is **0.4 for a normal item** (weapon / armour /
  consumable) — kept strictly worse than selling for Credits when a store is
  reachable, but better than nothing when it isn't — and **2.4 for trash
  loot** (`ItemType.Junk`), i.e. +500%. **Junk is convert-only** — no store
  buys it (`Store.BuyFromTraveler`/`Deposit` both refuse it outright; a
  `sell`/gear-sale path never applies to it) — so `convert`/`sell all` is
  the one and only way to turn it into anything, and at the old flat 0.4
  it was a poor trickle next to the travel bills a downstream push runs
  up, so clearing the floor after a fight now actually refuels you. Still
  replicates the "quasi semi-flawed but usable" economy the original was
  known for, without the exploitable parts.
- **Tachyon pool size** was scaled up end-to-end for the downstream push: the
  starting pool (`ClassDefinition.BaseTachyons`) tripled (+200%) and per-level
  growth (`TachyonsPerLevel`) ×6 (+500%, 4→24 for melee, 5→30 for the
  casters), then every class's starting pool was given a further flat +40
  on top once playtesting showed even the tripled pool ran dry after ~5–6
  fights (Soldier 20→60→100 … Scientist 34→102→142). A thin pool meant a
  botched overreach couldn't afford the retreat home and spiralled into a
  no-fuel death, and the old per-level trickle never re-opened the buffer. A
  level-10 Soldier's nominal pool is now 316 (was 56). The pool is uncapped
  regardless; these numbers only set the starting fill and the
  passive-regen ceiling.
- Time travel cost = `max(8, ceil(0.04 * |target_year - current_year|))`
  Tachyons, symmetric (retreating toward the present costs the same as
  advancing). Original tuning (0.2 → 0.1 → 0.04 across playtests; then an
  `8`-Tachyon floor added so a cheap decade-creep can't farm every year for
  free — a short hop now costs about what the ~200-year jump it should
  have been does). At 0.04 a one-tier early hop (~250 yrs) is affordable from
  level 1; a full cross-timeline leap costs ~120. With the pool now
  uncapped, that leap is a matter of stockpiling conversions rather than
  something a small pool forbids outright.
- `heal` restores HP at 3 HP per 1 Tachyon — no ratio survives in the
  historical record; 3:1 keeps healing a real competitor for the Tachyon pool
  without making the early game an attrition death (playtested; was 1:1).
- New characters start with a few `Field Ration` heal items so the first
  year isn't a pure attrition race before you can loot or buy any HP
  recovery of your own.

## 3. Movement & the world map

### 3.1 Grid and compass `[SOURCE]`
Each year's map is a 2D grid of rooms addressed by East/West and
North/South offsets from the origin, exactly matching the surviving
screenshot's `Compass: (2E : 0N)` readout. Movement commands are single-letter
directions: `n`, `s`, `e`, `w` (and optionally `ne/nw/se/sw` as a v1.1
stretch goal, not in the original).

- Rooms have short, atmospheric one-line descriptions (`You're in a
  maintenance shop.`, `You see rubble everywhere.`, `You feel a cold
  breeze.`) `[SOURCE style]`.
- Adjacent-room hints are surfaced before you move into them (`You see
  shadows to the east, west.`; `Something stirs to the north.` when a
  monster is next door) `[SOURCE style]`.
- Available exits are always listed explicitly, e.g. `north - area
  continues.` `[SOURCE style]`.
- **Monsters occupy rooms** (§7): the year you're in has monsters standing
  in specific rooms; `look` names the ones sharing yours and hints at
  neighbours; `fight [name]` engages one where you stand; loot left on a
  room's floor is listed and picked up with `take`.

### 3.2 The timeline & time travel `[SOURCE: confirmed mechanic + Tachyon cost]`
- The world is a **continuous timeline** from year **2000 A.D.** (the
  "present" city, where every character starts) to **5000 A.D.**. There are
  no discrete levels: difficulty, monster stats, and loot value all scale
  smoothly with the year. Year 2000 sits at scaling "tier" 1.0 and year
  5000 at tier 9.0, on a **piecewise curve that's steeper early** — one
  tier per 250 years through 2000–3000 (reaching tier 5), then one per 500
  years after — so a short early hop actually changes the fight instead of
  the first ~600 years all playing the same (`ChronoTravelers.Core.Time.TimeScale`).
- **Each year has its own grid map**, generated deterministically from a
  per-save **world seed** plus the year — the same year always produces the
  same layout, so it can be a pure function of the save with nothing about
  the geometry stored. Room descriptions are drawn from 14 authored
  **era bands** (The Fallout Belt → … → The Final Instant) that tile the
  3000 years; a year takes its theme and monster/loot pools from its band.
- Command: `travel <year>` (any year 2000–5000), `travel +N` / `travel -N`
  (relative), or `travel next` / `travel prev` (the next/previous
  Warden year).
- Cost: `max(8, ceil(0.04 * |target_year - current_year|))` Tachyons,
  deducted on success (coefficient lowered 0.2 → 0.1 → 0.04 across
  playtests so mid-range hops are affordable from low level — a one-tier
  early jump is ~10 Tachyons, a full cross-timeline leap still ~120 — then an
  `8`-Tachyon floor so a short hop isn't nearly free and worth spamming).
  Symmetric — retreating toward the present costs the same as advancing
  (this supersedes the earlier "retreat is free" rule now that travel is
  otherwise unrestricted). Insufficient Tachyons produces a warning and blocks
  the jump — the failure mode independently confirmed by a historical
  MBBSEmu bug report about a "warning when attempting to time travel
  without enough tachyons."
- **Travel is otherwise unrestricted**: no unlock, no minimum character
  level, no gate. How hard the fights get is the only limiter — and
  **overreaching is a deliberate option**: jumping well past your level
  band drops you among monsters (and loot, and gear) scaled far above you,
  a high-risk raid for a shot at better equipment. The console flags such
  a jump ("2450 A.D. is around tier 3 — scales to ~level 30, you're level
  1") and asks to confirm, but never forbids it.
- **Wardens** are still here, but as tough optional encounters rather
  than gates. The world seed places one every random 50–100 years across
  the timeline; a Warden year stations an automated temporal-defense
  construct (~3× a regular monster's HP for that year) guarding a
  guaranteed year-scaled **Legendary trophy** from a pre-collapse tech
  cache, present until you beat it once. It blocks nothing — travelling
  past a Warden year was never restricted.
  **Year 5000 is always guaranteed a Warden** regardless of where the
  random placement lands, and it's not just another randomly-scaled one:
  "The Convergence" (docs/ENDGAME_STRATEGY.md recommendation 4) is a
  unique, distinctly stronger capstone — 5× HP and 10× XP/Credit reward
  (vs. a regular Warden's 3×/5×), a higher-power Legendary trophy, and the
  `"paradox"`/`"capstone"` tags — the literal end of the timeline finally
  getting a fight to match its own room text.
- **Persistence**: map layouts are regenerated from the seed, not stored.
  What the save keeps per character is the world seed, the current and
  furthest-reached year, the set of cleared Warden years, and every
  store the player owns (which year, its Credit capital, and its
  listings — re-attached to the regenerated world on load).

### 3.3 Death & recall
- Dying drops a portion of unconverted inventory at the death location (loot
  becomes lootable by other NPCs/Travelers) and snaps the character back
  upstream to the year 2000 A.D. with an Tachyon penalty. No source material
  describes death handling, so this is original, tuned to punish but not
  erase progress.
  > **Implementation (2026-09-07):** `ChronoTravelers.Game.DeathRecall`,
  > shared by both front ends (the console's combat-defeat and
  > world-tick-ambush death paths, and the multiplayer server's
  > `SharedGame.Respawn`) so they can't drift apart on it. The "portion" is
  > half (rounded down) of the Traveler's *unequipped* inventory — the
  > currently wielded weapon, armor, and ranged weapon are never drop
  > candidates, so a recalled Traveler can still defend itself — dropped as
  > real ground loot (`YearPopulation.AddGroundLoot`) at the exact room/year
  > of death, `take`-able by anyone who finds it later, exactly like a
  > monster's own drop. The Tachyon penalty is half of current Tachyons,
  > rounded to the nearest whole. Level, stats, Credits, and equipped gear
  > are untouched; HP is restored to full on arrival at year 2000's start
  > room. Dying no longer ends the console session either — it's a setback
  > you keep playing through, not a game-over screen.

## 4. Character classes

The five classes are the **crew roles of a Project Meridian research
station**. Their *mechanical* shapes descend from the door game's five
confirmed classes `[SOURCE: Thief, Priest, Wizard, Warrior, Mage, per the
MBBSEmu wiki; "Barbarian/Cleric" appear in a second, looser source and are
treated as the same archetypes under different version-era names.]` — the
mapping is Warrior→**Soldier**, Thief→**Spy**, Priest→**Doctor**,
Mage→**Scientist**, Wizard→**Engineer**. All names, ability names, numbers,
lore, and level-gates below are original design filling a documented gap.

Every class shares: HP, Tachyons, a primary attack, an inventory, and access to
`convert`/`wield`/`sell`. Classes differ in HP/Tachyon scaling, their unlocked
ability tree, and which loot they can equip.

| Class | Role | Primary stat | Flavor |
|---|---|---|---|
| Soldier | Melee tank/damage | Strength | Station security — best HP, heaviest gear, cheapest Tachyon drain |
| Spy | Skirmisher/utility | Agility | Recon and infiltration — evasion, opening-strike crits, store contacts |
| Doctor | Support/healer | Resolve | Trauma medicine — group heals, combat stims, resonance vs. echoes |
| Scientist | Tachyon blaster | Intellect | Tunnel theory — high burst Tachyon-cost abilities, area damage, weak melee |
| Engineer | Systems utility | Intellect | Power and hardware — control/sabotage, rigged micro-jumps |

\* Scientist and Engineer are kept as two distinct Intellect classes (rather
than one) to honor the wiki's explicit 5-name list; differentiated by role
(control/utility vs. blaster) so they don't overlap mechanically.

### 4.1 Leveling
- XP from monster kills. Full value while the killer is within the band a
  tier is meant for (up to `character_level ≈ 10 * tier`); past that cap it
  falls off 8% per level over, down to a 10% floor
  (`MonsterScaling.KillXp`). So grinding a year long after you've outgrown
  it trickles — the XP is out where the fight is still real.
- **Credits from monster kills too** (original addition — previously a kill
  paid only XP, with Credits coming solely from selling loot). Scaled 1:4
  against XP (`MonsterScaling.CreditRewardPerTier` = 10 vs. `XpReward`'s 40
  per tier), and deliberately modest next to loot-selling income — a small,
  always-available trickle on top of it, not a replacement. Shares XP's
  exact outlevel falloff curve (`MonsterScaling.KillCredits`), so farming a
  trivial year never becomes a better Credit source than a fair fight.
  Sized so a single kill roughly covers ten ticks of a same-tier store's
  own Credit maintenance (§6.2) — grinding gives a direct, if modest, way
  to fund one.
- Soft level cap tied to progress: `character_level ≈ 10 * tier`, where
  `tier` is the scaling tier for the **furthest year the character has
  reached** (`TimeScale.SoftLevelCapForYear`, clamped to 10–60). Keeps
  power and depth loosely paired without hard-blocking grinding.
- **Hard level cap is 60** (`Leveling.MaxCharacterLevel`, raised 30 → 60).
  At the old cap a Traveler stopped growing around year 2500 and rode the
  back 5/6 of the timeline with frozen power; levels 31–60 keep the stat
  and HP/Tachyon curves climbing so year 5000 is a reachable target. The
  **ability trees now reach a 7th tier at level 35** (Engineer 25 —
  `Leveling.TopAbilityLevel`, docs/ENDGAME_STRATEGY.md recommendation 2);
  levels past that (36–60, Engineer 26–60) grant no new abilities (see
  §4.2). **Each class also gained a second wave of six passives across
  levels 33–58** (Engineer 31–56 — docs/ENDGAME_STRATEGY.md recommendation
  1), smaller top-ups on the same hooks as their level-1–28 counterparts
  so the two stack (see §4.2.1). The XP curve is quadratic through level 25
  (`Leveling.XpCurveKneeLevel`) then holds a flat per-level cost, so the
  deep levels are a linear grind rather than a quadratic wall — levels
  1–25 cost exactly what they did before the raise.
- Every level grants a stat increase — **+2 to the class's primary stat,
  +1 to each of the other three** (`Leveling.PrimaryStatGainPerLevel` /
  `SecondaryStatGainPerLevel`). So a veteran's defence and speed (both
  Agility-derived) keep pace with the deep-future curve instead of
  staying frozen at the class base. Every **5th level** also grants a new
  class ability (see §4.2).
- **HP growth tapers.** Full `HpPerLevel` through level 15
  (`ClassDefinition.HpGrowthKneeLevel`), then half rate to the cap — a
  flat-linear pool ran away from what any deep-future monster could
  threaten (a level-30 Soldier had ~10× base HP under the old curve).
  `HpPerLevel` is spread by class identity so the durability order holds
  but every class climbs steeper: **Soldier 9, Spy 7, Doctor 7, Engineer
  5, Scientist 4** (was 6/5/5/3/3). A level-30 Soldier is now ~223 HP; at
  the level-60 cap, ~358. The half-rate tail still keeps the deep pool
  bounded against the superlinear monster scaling (§6), it just sits
  higher.
- **Permanent stat elixirs** ("Meridian Serum") — Epic consumables, **two
  per year on the floor** (`TimelineContentFactory.StatElixirsPerYear`),
  placed the same protected way as the Time Shard (monsters and NPCs
  can't take them), so "half as rare as the Shard." `use` one and it adds
  **+5** to a stat permanently — no timer, it rewrites the `StatBlock`
  exactly as a level-up does, and is saved with the rest of `Stats`. The
  only non-level way to grow a stat.
  > **Which stat is chosen when you drink it, not when it spawns.** A
  > serum found on the floor doesn't arrive pre-labeled to one of the four
  > stats any more (`ConsumableEffectType.BoostChosenStat`,
  > `TimelineContentFactory.StatElixir(Random, int)`); `use` prompts for
  > Strength / Agility / Resolve / Intellect at the moment it's drunk. This
  > closes a real balance gap: only a class's own primary stat (attack) or
  > Agility (defense + turn order, universal — see `EffectiveDefense`/
  > `Speed`) does anything mechanically for a character, so a pre-rolled
  > serum was a dead item for four of the five classes roughly half the
  > time, and for Spy — whose primary *is* Agility — three-quarters of the
  > time. Choosing at drink time makes every serum useful to whoever finds
  > it, for any class. The fixed-stat overload
  > (`StatElixir(PrimaryStat, int)`, `ConsumableEffectType.BoostStrength`
  > etc.) still exists for content that wants to hand out a specific known
  > boost. Strength, Resolve, and Intellect still only feed their own
  > class's attack formula (Agility remains the only stat with a universal
  > effect) — widening what each stat *does* is a separate, not-yet-done
  > follow-up from this fix.

### 4.2 Ability trees (original design; 7 tiers per class = levels 5/10/15/20/25/30/35)

> **Level cap 60, ability trees now 7 tiers.** The hard level cap was
> raised to 60 (§4.1), and a 7th tier was since added at level 35
> (Engineer 25) per docs/ENDGAME_STRATEGY.md recommendation 2 — a stronger
> variant of that class's existing kit rather than a wholly new mechanic
> (`source: "endgame"` in `abilities.json`; see the per-class list below).
> Levels past the 7th tier (36–60, Engineer 26–60) are still stat/HP
> growth only. This was "Reaching Year 5000" design-plan Step 2's
> ability-tree half; §4.2.1 covers the passive-trait half.

> **Engineer exception:** its tree unlocks on an accelerated schedule —
> **levels 2 / 5 / 9 / 13 / 17 / 21 / 25** (the level-25 7th tier added
> per recommendation 2, keeping the same compressed cadence) — because the
> Engineer is the frailest melee class (18 HP, Intellect-primary) and had
> no answer to a bad early fight before its kit came online. Dampener
> (slow the target so you strike first) at level 2 and Sabotage (cut
> incoming damage) at 9 are the survival tools it needs early (playtest
> feedback). See `src/ChronoTravelers.Content/abilities.json`.

Example — **Soldier**:
1. Lv5 — *Suppressing Sweep*: rake fire across the target and up to 2 others crowding it.
2. Lv10 — *Field Patch*: once per fight, a trauma seal for 20% max HP — no Tachyons.
3. Lv15 — *Armor-Piercing Rounds*: sabot loads punch through part of the target's plating.
4. Lv20 — *Fire Discipline*: call the shots — nearby allies hit harder for a while.
5. Lv25 — *Breach Stance*: set behind the shield — incoming damage drops.
6. Lv30 — *Confirmed Kill*: heavy bonus damage vs. targets below 25% HP.
7. Lv35 — *Executioner's Volley* (endgame): past Confirmed Kill — even heavier bonus damage vs. a target below 25% HP, at a proportionally higher Tachyon cost.

Example — **Doctor**:
1. Lv5 — *Triage*: focused single-target heal (Tachyon cost).
2. Lv10 — *Combat Stim*: your strikes land harder for a while.
3. Lv15 — *Purge Echo*: a resonance burst — devastating to `echo`-tagged monsters.
4. Lv20 — *Broad-Spectrum*: field-treat yourself and every ally in the room.
5. Lv25 — *Crash Cart*: bring a downed ally NPC back on partial vitals (rare, long cooldown).
6. Lv30 — *Iso Field*: a sterile bubble — a brief window of total immunity.
7. Lv35 — *Field Sanctuary* (endgame): past Broad-Spectrum — a much larger field heal.

(Spy, Scientist, Engineer get parallel 7-tier trees — full tables live in
`docs/CONTENT_PLAN.md` so this GDD stays a living-but-stable reference; the
pattern — 6 tiers, one per 5 levels, escalating from single-target to
area/group to a capstone, plus a 7th "capstone-plus" endgame tier added
per docs/ENDGAME_STRATEGY.md recommendation 2 — is the standard every
class follows: Spy's *Vanishing Act*, Scientist's *Cataclysm*, Engineer's
*Overload Discharge*.)

> **Implementation (2026-09-07) — the three non-combat abilities.** Crash
> Cart above, Spy's *Black Market Contacts* (Lv25: "permanent, better buy
> and sell prices at every store"), and Engineer's *Jump Rig* (Lv5: "a
> rigged micro-jump — short teleport to escape or reposition") shipped as
> `effect: "None"` — docs/CONTENT_PLAN.md flagged them as having no honest
> translation into the round-by-round fight loop. None of the three
> actually needed one: each is a party/economy/overworld mechanic, not a
> combat one. **Black Market Contacts** needs no cast at all — it's a flat
> +8% store-price bonus (`Traveler.StoreDiscountBonus`) that turns on
> automatically once a Spy hits level 25, stacking with the Light
> Fingers/Silent Partner/Underworld Ties/Broker's Network passives (§4.2.1)
> for up to 28% total by level 58; the catalog entry stays `effect: "None"`
> since there's genuinely nothing to cast. **Jump Rig** and **Crash Cart**
> are cast with a new out-of-combat `cast <ability>` command — both the
> console and the multiplayer server, `fight`'s auto-resolve there meant it
> never previously needed any ability-casting command at all — via
> `ChronoTravelers.Engine.Combat.OverworldAbilityResolver`: Jump Rig
> (`effect: "ShortTeleport"`) teleports to a random room within 3 exit-hops
> of the caster's current position (`LevelMap.RoomsWithinHops`, new — real
> teleport, not a walk, so it isn't blocked by anything in between); Crash
> Cart (`effect: "ReviveAlly"`) heals the most-wounded living NPC Traveler
> sharing the caster's room up to 50% max HP. **Judgment call, flagged:**
> "downed" is read as badly wounded (≤25% max HP) rather than literally 0
> HP — a genuinely dead NPC is replaced wholesale by the very next world
> tick's respawn (§3.3-adjacent machinery, `WorldSimulation.RespawnDeadNpcs`),
> nowhere near enough of a window for a player to notice a kill and react
> with a cast before the "ally" is simply a different NPC. Targeting the
> room's worst-off-but-still-standing NPC instead is the interpretation
> that's actually usable in play.

### 4.2.1 Passive traits (always-on, no activation/UI/AI decision)

Each class also has a table of **always-on passive traits**
(`ChronoTravelers.Core.Characters.PassiveTrait`/`PassiveTraits`, no
content file — see docs/CONTENT_PLAN.md for why) unlocked automatically by
`Level`, read live wherever their `PassiveHook` applies (`Traveler` for
most, `Economy.Store` for the two store hooks, `Engine.Npc.MonsterController`
for the aggro/ambush hooks that live outside a single fight) — never by
switching on a trait's name. **12 per class**: a first wave of 6 (levels
1–28, Engineer 1–19), each roughly midway between a pair of active-ability
unlock levels, and a second wave of 6 (levels 33–58, Engineer 31–56) added
per docs/ENDGAME_STRATEGY.md recommendation 1 — smaller top-ups on the
same hooks as their first-wave sibling (so the two stack, e.g. Soldier's
level-1 Hardened +20% Defense-from-armor and level-33 Reinforced Plating
+10% sum to +30%), matching each class's flavor (Soldier leans defense/
ambush/armor; Doctor heal/tachyon; Spy economy/speed/aggro; Scientist
tachyon/caster/crit-economy; Engineer defense/speed/junk/cast-discount).

One second-wave passive per class introduces a genuinely new hook rather
than topping up an old one: Soldier's level-58 **Anomaly Killer**
(`PassiveHook.ParadoxDamageBonusPct`, +18% attack damage vs. a
`"paradox"`-tagged monster) — the mechanical hook for the paradox theme
(§3.2/`eras.json`'s Chronofracture/Long Now/Final Instant bands, years
4600+) that recommendation 3 asked for, mirroring how Scientist's existing
Field Calibration reads the `"caster"` tag. `TimeWorld.Build` tags every
monster spawned in a paradox-themed era/year, same append pattern as
`"caster"` (see `TimelineContentFactory.ForSpecies`).

### 4.3 Restrictions (apply identically to player and NPCs)
- Weapon/armor equip requires class-tagged gear (a Scientist can't wield the
  Soldier's breaching maul at full effectiveness — non-class gear works at a
  penalty rather than being hard-blocked, to keep loot from feeling wasted).
- Tachyon pools and drain rates differ per class (Scientist/Engineer drain
  faster from ability use; Soldier/Spy drain slowest, lean harder on raw HP).

## 5. Loot system `[SOURCE: wield/sell/convert]`

- **Sources of loot**: monster drops on defeat (a themed table per
  monster, built from item archetypes and scaled to the **year** it's
  fought in); **on-load floor loot** — when a year is first entered,
  ~a third of its grid rooms are seeded with one random (rarity-weighted,
  year-scaled) item each so a year never feels empty; and **random
  location spawns** (a periodic chance per room, per tick, for an item to
  appear on the ground). Defeated-monster loot — the rolled drops plus
  anything the monster had scavenged — **falls to the floor where it
  died**; nothing auto-enters the player's pack. `look` lists it and
  `take <item>` / `take all` picks it up. (NPC grinding is abstract and
  off-grid, so an NPC's kills still go straight into its inventory.)
- **Time Shard** (one per year, on the floor): a Legendary melee weapon
  whose AttackBonus is 1.25× the strongest weapon of any kind available
  that year, and whose Credit value scales with the year. Monsters and
  NPCs never pick one up — it's the player's to take, wield, or sell.
- **Meridian Serums** (two per year, on the floor — see §4.1): Epic
  consumables that permanently add +5 to one stat. Placed and protected
  the same way as the Shard; "half as rare" only in that there are two,
  not one.
- **Drop composition**: a regular monster's table is built by category so
  a kill always pays and occasionally supplies you — a **guaranteed
  sell/convert fodder** piece (a junk item, drop chance 1.0), a real
  chance at a second (~0.35), then a **weapon/ranged** roll (rarity-weighted)
  at ~0.35, a separate, independent **armour** roll at ~0.25, then a
  **consumable** at ~0.20. Weapon/ranged and armour roll independently
  (playtest finding) rather than sharing one combined "gear" slot — a
  single shared roll, skewed by how weapon/ranged-heavy the archetype
  catalog is, left most species unable to ever drop armour at all. Every
  kill leaves at least one thing on the body worth taking — and the drop
  roll has a hard backstop (`LootDropRoller.RollForKill`): if every entry
  somehow misses it forces the likeliest one, and a monster with no table
  at all still yields a tier-scaled scrap. If a species' theme pool lacks
  a category the world generator borrows the cheapest one from the full
  catalogue, so every monster can also drop something to wear and
  something to use.
- **Scaling**: an item's `tier` is derived from the **year** it was
  generated in (`TimeScale.TierForYear`, a continuous 1.0–9.0 across
  2000–5000); tier drives base stats, sell price, and Tachyon-conversion value
  via `LootScaling` (value baseline `12 * tier + 10` — the flat term is a
  playtest bump that roughly doubles tier-1 loot so an early grind funds a
  real purchase, tapering to ~1.3× by tier 9), so loot from year 4000 is
  categorically better than loot from year 2100 — this implements "loot
  scales based on time travel level" against the continuous timeline.
- **Disposition**: every lootable item supports the same three verbs as the
  original — `wield` (equip if class-compatible), `sell <item>` (at any
  store, price is store-and-negotiation-dependent, see §6), `convert <item>`
  (destroy for Tachyons, value per §2.1). `convert` works on any item
  regardless of type; `sell` works on anything **except Junk** — no store
  buys it, so `convert`/`sell all` (which now converts rather than sells)
  is its only disposal path; `wield` makes sense for Weapon/Armor/Ranged.
- **Ranged weapons** (original addition, enabled by §7.1's spatial
  monsters): wands, bows, and — in later years — guns occupy their own
  equip slot alongside the melee weapon. `wield` one, then `fight <name>`
  **locks it in as your ranged target instead of starting blocking melee**
  ("You draw a bead on the Ashfall Behemoth.") — `fight` only opens the
  turn-by-turn melee loop when no ranged weapon is readied. From there
  `point <dir>` (wands) or `shoot <dir>` / `fire <dir>` (bows/guns) fires
  at the locked target specifically, down a **straight, unbroken chain of
  room exits** in that direction, out to the weapon's own **range (1–4
  rooms** — content-authored per weapon, ChronoTravelers.Content's item
  archetypes catalog; more powerful weapons reach farther). Nothing fires
  (and no ammo is spent) if the corridor breaks early or the locked target
  isn't out that way within range — an out-of-combat action for kiting: line
  up, back off, keep firing, all before it's ever adjacent. Each ranged
  weapon carries a **finite built-in magazine** (no separate ammo item);
  every shot spends one round and the count persists in the save. A ranged
  shot may also carry an effect — `Weaken` leaves the target fighting at
  reduced defence for its next `fight`; `Stagger` is the offense-side
  counterpart, leaving it fighting with reduced attack instead. Either way
  the same `effectMagnitude` field doubles as the shot's own damage
  multiplier, so a stronger effect also hits harder. Once empty the weapon
  can't fire and is worth only a fraction (down to 25%, scaling with
  rounds spent) on `convert`/`sell`. Damage-wise wands and guns pierce
  armour; bows don't. A kill from range drops the loot on the target's
  floor — walk in and `take` it. Every era theme ships its own small
  ranged ladder now (docs/CONTENT_PLAN.md's item archetypes catalog),
  rather than the handful of early sample weapons.
  - **A hit that doesn't kill provokes a reaction**, not just aggro: caught
    sharing the shooter's room, the target goes straight to Hostile — no
    use running. Hit from a room away, it either **pursues** (closes the
    distance every tick, ignoring the normal aggro-range shadowing limit,
    for up to ~40 ticks before giving up) or, if the hit drops it to a
    fraction of its HP some species are prone to breaking under (an
    archetype-based default — Bruisers never do; see
    docs/CONTENT_PLAN.md's monster generations), **flees** instead (steps
    away every tick until it heals back up, dies, or the player leaves). A
    pursuing monster yells a threat at the player every tick it's closing
    in; a fleeing one yells something like "Get away from me!" instead —
    both unconditional per tick, distinct from the ambient monster-to-
    monster banter in §7.1.
- **Consumables** (original addition — not in the source material) get a
  fourth verb instead of `wield`: `use`/`eat`/`drink <item>`, which
  triggers the item's effect and destroys it. Beyond the original instant
  flat HP heal ("food") and temporary attack/defense buff ("potion",
  lasting a set number of world ticks), the catalog now also has: a
  temporary Speed buff (quickens turn order in combat); an instant flat
  Tachyon refill (a "battery"/"cell" — the Tachyon-pool counterpart to a
  heal item, pure income, no cost); and a heal-over-time tonic (heals a
  flat amount on every tick for a duration, rather than all at once). This
  is separate from — and doesn't cost Tachyons like — the `heal` command
  in §2/§2.1; any consumable is a one-time item you have to loot or buy
  first.
- Rarity bands (Common/Uncommon/Rare/Epic/Legendary) — original addition,
  since the source material never specifies a rarity system, but a
  near-universal expectation in a modern loot-driven RPG.
  - **For equippables (weapon / armour / ranged), rarity is *derived from
    power*, not authored.** Each archetype carries a `powerMultiplier`
    (~0.5 "crude" → 1.0 "standard" → ~1.8 "fine" → ~2.9 "relic"); it
    scales the per-tier combat baseline (`LootScaling.EquipBonusFor`,
    clamped to a 0.3×–3.0× band), and `Rarity.ForPower` names the band. So the
    weakest weapon in a year does roughly half a baseline hit and the
    best does several times it, and its rarity label always matches its
    damage.
  - **Rarity then governs how often the archetype is rolled onto a
    monster's loot table** (`Rarity.DropWeight`: Common ×6 … Legendary
    ×0.25), so a relic-class weapon is a genuine rare/unique find rather
    than just a different colour. Government depots stock a dependable
    mid-grade (Uncommon) piece; the extremes are loot only. A
    Warden's guaranteed trophy sits deep in the Legendary band.
  - Consumables and junk keep an authored rarity (a potion's strength
    isn't a damage number).

## 6. Stores & economy `[SOURCE: purchasable NPC-run stores + currency]`

### 6.1 NPC supply stores
- Every year has a supply store — a Meridian-era government depot still
  running on automation, or the settlement that grew up around one — placed
  deterministically by the world seed `[SOURCE]`. These are the baseline
  places to sell loot for **Credits** and buy consumables/basic gear; each
  stocks the same staple kinds (a heal item, an attack booster, a defense
  booster, a weapon, an armour piece), pulled from the year's era themes.
- Prices scale with the **year** (a year-4000 store deals in year-4000-tier
  goods and pays/charges accordingly) — this is how "an economy based on
  the time travel level" gets implemented against the timeline.
- Selling to a store nets +30% over an item's own value
  (`EconomyPricing.SellRateMultiplier`, original tuning — the GDD leaves
  the exact sell price "store-and-negotiation-dependent," per §5) — selling
  loot is the primary Credit faucet, so it's tuned above a flat 1:1. The
  same multiplier also scales what a store pays buying surplus from a
  passing traveler and what it then asks reselling it
  (`Store.BuyFromTraveler`/`DefaultAskingPrice`), keeping their relative
  spread — and the §6.3 Credit-sink margin it depends on — unchanged. **No
  store buys or lists a Junk item, ever** (`BuyFromTraveler`/`Deposit`
  both refuse it outright) — junk is convert-only (§2.1/§5); this doesn't
  apply to any other item type.

### 6.2 Player-owned stores `[SOURCE: players can buy the NPC stores]`
- Every year offers, beyond its always-open depot store, a limited number
  of purchasable store slots (`TimeWorld.BuildStores`; the count is
  content-authored — `store-templates.json`'s `playerSlotCount`, 3 by
  default — not an unbounded supply). A Traveler (human or NPC) can
  purchase an available slot for a Credit cost scaled to that year's tier.
  A human player's owned stores persist across sessions (§3.2).
- Once owned, the owner stocks it manually: **stock** an item from
  inventory at an asking price, **withdraw** it back, or **reprice** a
  listing.
- **Maintenance & foreclosure**: a player/NPC-owned store (the depot store
  is exempt) costs Credits per world tick to stay open, drawn from a
  dedicated maintenance reserve the owner tops up with **charge**. Go
  unfunded for 10 consecutive ticks (`Store.ForeclosureThreshold`) and the
  store is repossessed — the slot goes back up for sale, and its unsold
  listings stay attached for whoever buys it next rather than vanishing, so
  a lapsed owner doesn't erase a stranger's future find. Originally a
  Tachyon cost; switched to Credits (`EconomyPricing.MaintenanceCostPerTick`)
  so a store's upkeep draws on the same currency its Capital/sales already
  deal in, rather than competing with the owner's own separate
  survival/travel/heal Tachyon budget.
- **deposit** funds a store's Capital (the pool it buys from other
  Travelers with) directly from the owner's own Credits — the funding
  counterpart to **stock**'s item-listing side.
- **collect** withdraws a store's accumulated Capital (from NPC/player
  sales) into the owner's own Credits — the idle-income loop below.
- **NPC shoppers** periodically path to player-owned stores and buy/sell
  based on their own needs (an NPC low on a class-appropriate weapon will buy
  one if the store has it and the price is within their budget heuristic; an
  NPC with genuine surplus gear will sell it to a store with open capital) —
  this is the "NPCs will sometimes visit and buy and sell from the player
  stores" requirement, made concrete. An NPC over-encumbered with junk
  converts it for Tachyons instead (`NpcController.Act`'s standalone
  excess-junk check) — no store ever buys junk, so this doesn't need a
  store visit at all.
- **NPCs also buy and run stores themselves**, same as a human player:
  buying an open slot, charging its maintenance, stocking surplus gear, and
  collecting Capital (`NpcController.TryPurchaseStoreSlot` /
  `TryTendOwnStore`). They never take the *last* purchasable slot in a
  year — one always stays open for the human player, on top of the
  always-open depot store (resolves the §12 open question below: capped by
  this reservation rule, not an outright ownership count).
- Store owners collect Credits from NPC sales automatically (idle-income
  loop) and can visit in person to restock/collect/adjust prices.

### 6.3 Economy safeguards (original, informed by the source's known flaw)
The original economy was described by a contemporary player as "quasi
semi-flawed." To avoid reproducing that: NPC store customers have a budget
cap per visit, sale prices are clamped to a level-appropriate band (no
selling level-1 junk into a level-10 store for level-10 money), and Credit
sinks exist (store purchase cost, restocking depot inventory, repair costs)
so currency doesn't purely inflate. Store maintenance (§6.2) is another such
sink, drawn from the same Credit economy — an owned store nobody ever funds
eventually stops being an asset at all.

> **Implementation (2026-09-07):** "Repair costs" is now a real mechanic,
> not just a line item. A melee Weapon or Armor piece (ranged weapons are
> exempt — their existing ammo-depletion economy already models wear)
> carries its own Durability that ticks down by 1 with every hit it lands
> or takes in combat (`ChronoTravelers.Core.Characters.Traveler.DegradeEquippedWeapon`/
> `DegradeEquippedArmor`). As it wears, its combat contribution scales down
> linearly to nothing at 0 Durability (`Item.DurabilityEffectiveness`) —
> still equippable (the same "penalty, not a hard block" philosophy as
> off-class gear in §4.3), just useless in a fight until repaired. Its
> scrap/sale value degrades on the gentler 25%-floor curve the ranged-ammo
> model already uses (`Item.ValueFraction`), so a broken piece is still
> worth something to a store even if it's worthless in a fight. `repair
> <item>` at any store (`ChronoTravelers.Core.Economy.Store.Repair`)
> restores full Durability for Credits — priced at up to half the item's
> own Value depending on how worn it is
> (`EconomyPricing.RepairCost`/`FullRepairCostFraction`), and, like store
> purchase cost and maintenance, those Credits leave the economy outright
> rather than moving into the store's Capital. Starter gear
> (`ChronoTravelers.Game.CharacterFactory`) is deliberately built without
> Durability tracking, so a fresh character's opening loadout never breaks
> on them. The 1-point-per-hit wear rate and the 50%-of-Value repair
> ceiling are original tuning, like every other combat/economy curve in
> this document — not derived from a specified formula.

> **Implementation (2026-09-07):** "Restocking depot inventory" is now
> real too. Every world tick, `ChronoTravelers.Engine.Simulation.WorldSimulation`'s
> new restock pass (`ApplyGovernmentRestocking`, run alongside the
> existing maintenance pass) checks every visited year's *government* depot
> — never a player-owned store, which restocks by the owner's own `stock`
> command or an NPC owner's automatic tending (§7's `TryTendOwnStore`) —
> for any of its six staple categories (heal / attack potion / defense
> potion / weapon / armor / ranged) currently missing from the shelf, and
> refills it (`TimeWorld.RestockGovernmentDepot`). The depot pays
> `EconomyPricing.BuyPrice` for the new stock out of its own Capital
> (`Store.RestockGovernmentSupply`) — the same "acquire, then mark up to
> resell" economics as a normal purchase from a traveler, just from an
> abstract wholesale supplier instead of a pack, which is what makes it a
> Credit sink and not a transfer: a government store's Capital isn't
> circulating currency any player can collect back out. Silent (no
> broadcast) since it can fire every tick — a message every couple of
> seconds would drown out the rest of the feed.

## 7. NPC simulation ("simulated players")

Since v1 has no network multiplayer, the world needs to feel alive:

- A configurable population of NPC Travelers exists (a single `totalCount`,
  default 5), each a full character with class, level, inventory, and
  Tachyon pool — built on the *exact same character/inventory/ability code
  path* as the human player, per the requirement that NPCs "play like
  players, with the same character classes and restrictions." The first
  `NpcPopulation.LocalPopulationTarget` (5) population slots are the **local
  pool**: they actively gravitate toward wherever the player currently is
  (or, on the shared-world server, a rotating occupied year) so their
  fights, travel, and — most importantly — store sales are things the
  player actually runs into, rather than statistically-almost-never across
  a 3000-year timeline. Any slots beyond that (only reachable if a designer
  raises `totalCount` past 5) keep the original whole-timeline scatter as
  background flavor. A local-pool NPC that dies respawns back near the
  current anchor year (`NpcPopulation.RespawnNear`, within
  `LocalSpawnSpreadYears` of it); a background-pool NPC respawns from a
  fresh whole-timeline draw (`NpcPopulation.RespawnScattered`), same as its
  original spawn.
- Each NPC runs a lightweight behavior loop each tick: assess Tachyon level (seek
  conversion fodder or a store if low), assess HP (retreat/heal if low),
  otherwise pursue its current goal — wear a better weapon/armor/ranged item
  already sitting in its pack the instant it's looted (no store needed),
  tend a store it already owns here (pay down Credit maintenance, stock
  surplus gear, collect Capital — §6.2), occasionally buy an open store slot
  if it doesn't own one (never the year's last one), trade at a year's store
  (selling genuine surplus gear it can't use before falling back to excess
  junk, and buying a weapon if unarmed), grind monsters in its year, or hop
  along the timeline. A local-pool NPC not
  already at the anchor year rolls a much higher travel chance and, when it
  rolls, heads straight for the anchor — the full jump if it can afford the
  Tachyon cost, otherwise the biggest hop toward it it can afford, so the
  gap keeps closing across ticks. A background-pool NPC (or a local-pool one
  already at the anchor) keeps the original low-chance, mostly-forward
  random hop.
- NPCs participate in the same kill-feed / **fray-band broadcast** channel
  as the player `[SOURCE: cross-board telepathic messages]` — "An Ashfall
  Echo was slain by a Dune Stalker," "Fang reached level 12," "Static
  jumped downstream to 3200 A.D." — so the leaderboard and the "who's
  doing what" feel of the original survives without a live human
  population. Feed names are cleaned up: a monster (common noun) reads
  "a/an <name>", capitalised at the start of the line; a named Traveler
  (proper noun) stays bare; the internal NPC instance suffix (" 2") is
  dropped. (The rupture leaks a low signal every
  Traveler's rig can pick up; that's the in-fiction reason you hear it.)
  Every broadcast is tagged with the **year** it happened in. The console
  shows only events in the player's *own* year inline after a command
  (plus any ambush on the player); everything happening elsewhere on the
  timeline is collapsed to a "…and N more" count, with the full feed on
  `news`. Keeps the moment-to-moment log about the room you're in.
- NPC decision-making is intentionally simple and rule-based for v1 (finite
  state machine, not full pathfinding AI/ML) to keep it debuggable and cheap
  to simulate at scale; an upgrade path to smarter behavior is a documented
  v2 idea, not a v1 requirement.

### 7.1 Spatial monsters
The year the player is standing in also runs a live monster population
(`ChronoTravelers.Core.Time.YearPopulation`, seeded deterministically from the
world seed on first entry, kept alive in the session's year memo — not
saved):
- Monsters occupy specific rooms and, each tick, **drift** through exits,
  **scavenge** off their room's floor, or — if hurt — **heal** from their
  own Tachyon pool, first **converting** a carried item if they're out of Tachyons
  (the same `heal` / `convert` the player uses). A monster only takes loot
  for a reason: **one item to burn for Tachyons** when it's low (it prefers
  junk/consumables, leaving a good weapon for you), or **a single weapon
  that beats what it's wielding** — a scavenged weapon adds its bonus to
  the monster's hits (`Monster.EffectiveAttackPower`) and drops with it on
  death, and the weapon it replaced falls back to the floor. A calm,
  full-Tachyon monster with a decent weapon walks straight over a pile. Drift
  is deliberately
  **slow and random** — a low per-tick move chance, no fixed heading,
  frequent multi-tick pauses — so a monster you spotted on the `monsters`
  list is still near where it was when you get there, rather than a
  same-speed target you can never catch. The `monsters` list shows each
  one's exact room (and the way it last stepped).
- **Every spatial monster is individually named** — `Monster.Enumerate`
  appends a three-digit `-###` callsign to its species name the moment it's
  seeded or trickle-respawned ("Ashfall Echo-042"), drawn from the same
  deterministic per-year stream everything else in that year's population
  uses, so re-seeding the same world/year reproduces the same callsigns.
  This replaces the earlier design where every monster of a species shared
  one plain name; it exists so a monster can be addressed individually —
  most importantly by the yell banter below. The Warden and a transient NPC
  "grind" opponent (fought and discarded within one tick, never placed on
  the map) are left un-enumerated — a boss and a nobody-sees-it fight don't
  need a callsign.
- **Monsters yell** — every tick, the year the player is standing in has a
  small (~12%) chance that one living monster calls out another by its
  enumerated name — a mix of hunting threats and plain insults ("Ashfall
  Echo-042 bellows, 'Junk Golem-017, I am looking for you!'"). It's not
  proximity-gated like the movement/earshot narration above — it's ambient
  flavor for the whole year, meant to make the place sound inhabited without
  interrupting every command with a line. Needs at least two living
  monsters; a year down to its last one goes quiet.
- **Monster fights stay relevant** — four linked knobs (with the HP-per-
  level taper in §4.1), tuned so a same-tier fight costs real HP without
  the early game getting harsh:
  - `MonsterScaling.BaseAttackPower` / `BaseHp` / `BaseDefense` are no
    longer independently hand-tuned polynomials — a second, superseding
    tuning pass **derives them from what a level-matched character's own
    attack/defense actually are** at that tier (`ReferencePlayerAttack`/
    `ReferencePlayerDefense`, averaged across the five classes), so the
    two sides of combat can't silently drift apart again. A regular
    monster's defense is 30% of the level-matched player's own attack
    (`DefenseFractionOfPlayerAttack`); its attack power is the
    level-matched player's own defense divided by 0.70
    (`AttackToPlayerDefenseRatio`); its HP is set to exactly
    `HitsToKillMonster` (3.0) of the player's own mitigated hits. At tier
    9 (level 60) that's a regular monster with ~84 attack, ~52 defense,
    and ~401 HP (vs. tier 1's ~11 attack / ~6 defense / ~48 HP).
  - `CombatResolver.RollDamage` mitigates with a smooth, ratio-based
    diminishing-returns curve (`attack² / (attack + defense)`), not a
    floor: full attack lands when defence is 0, half lands when defence
    equals attack, and it keeps falling — never to a hard floor or to 0 —
    as defence climbs further past it. This replaced an earlier
    `max(attack − defense, 0.30 × attack)` armour-penetration floor that
    turned out to be the permanent state of nearly every fight at every
    tier (a flat 2–4 damage regardless of how deep the timeline got) once
    attack and defense were tuned on separate numeric scales.
  - **Deep-tier starter weapons** (`TimelineContentFactory`,
    `MaybeStarterWeapon`): from tier 4 up, a monster has a rising chance
    (~15% → ~85%) of spawning already wielding a modest weapon — adds to
    its `EffectiveAttackPower`, drops as loot on death, deterministic per
    species/year.
  - Net: a regular monster now costs almost exactly `HitsToKillMonster`
    (3.0) of a level-matched character's own mitigated hits, by
    construction, at any tier — an armed monster (tier 4+) gets extra
    swings back at the player on top, but doesn't change how many hits it
    takes to fell it. Apex / Warden HP scale off that same baseline
    (tier-9 regular ≈ 401 HP; apex ≈ 962, Warden ≈ 1203) into longer
    fights.
- A few years also seed one or two **apex** monsters (`Monster.IsApex`,
  named "Frayed &lt;species&gt;"): much tougher (~2.4× HP, harder hits,
  ~3.5× XP, a loot table that reliably yields real gear biased to the
  strong end of the pool), but they accrue aggro at ~15% of the normal
  rate and drift half as often — so they essentially never provoke and sit
  as a findable landmark. The player chooses to take one on for the loot,
  or walks past. A bare `fight` never targets the apex; you name it.
- **Movement is narrated** relative to you, in the source game's style —
  a monster **first coming within one room** ("you hear something to the
  north," with varied phrasing), **entering** your room ("a Rubble Hulk
  comes in from the south"), or **leaving** it ("the Feral Runner slips
  away east"). `look` also lists what's stirring in each adjacent room.
- Monsters do **not** automatically pursue or attack anyone who walks
  past. Each carries an **earned aggro meter** toward the player
  (`ChronoTravelers.Core.Monsters.AggroModel`), raised by:
  - **stepping onto its tile** — the big one; do it over and over (pacing
    a chokepoint, farming a spot) and it stacks faster than it decays;
  - **lingering** on or next to it (a small trickle per tick);
  - **shooting it** with a ranged weapon (a large jump — it noticed).
  Moving a couple of rooms away, or ducking into a store, bleeds the
  meter back down (faster than it builds), and it never persists across a
  visit. Three bands:
  - **Calm** (default) — wanders, ignores the player entirely.
  - **Alert** — shadows the player (moves to close the distance) but
    takes no swing.
  - **Hostile** — also lands one **ambush** hit (half the player's
    defence, rate-limited to every other tick) — but *only* on a turn the
    player spent **idle** (`look` / `status` / `wait` / `inventory` / …).
    Acting — moving, `fight`, `heal`, shopping, `wield`, `travel`, `take`
    — is always safe, as is the turn you arrive and any room with a
    store. The `monsters` list shows each one's current mood.
  The point: the world is placid until you provoke it, the threat is
  legible and escapable, and none of it replaces the deliberate `fight`.
- Two monsters sharing a room may **fight each other**; the loser dies and
  its carried items plus a loot-table roll drop on that room's floor,
  exactly as when a player kills it, and it posts to the same kill-feed.
- A slow **respawn trickle** keeps an emptied year refilling toward a soft
  cap (~2/5 of its rooms, minimum 4) without ever overflowing.
- **Every year that's been instantiated this session keeps simulating**,
  not just the player's. The player's year runs the full loop (drift plus
  aggro / shadowing / ambush / player-local narration); every other year
  an NPC is in, or the player has passed through, runs an *unattended*
  loop each tick — monsters there still drift, fight each other, heal,
  grab ground loot and respawn, and their kills post to the shared feed
  (tagged with the year, so the console keeps them in `news` rather than
  the inline feed). A year nobody has entered stays dormant until someone
  does. Nothing about any of this is saved. NPC Travelers still grind
  abstractly against their year's roster rather than the placed monsters
  (spatial NPC↔monster combat is a follow-up).

## 8. Leaderboards `[SOURCE: MutantLink cross-board high scores]`

Displayed on the game's start/title screen, refreshed each session:
- **Furthest Year Reached** (all-time, across player + NPCs).
- **Highest Character Level** (all-time, across player + NPCs).
- Both boards show top 10, with the human player's own best highlighted even
  if outside the top 10.
- Data persists in the save file/local database so a leaderboard has meaning
  across NPC-simulated "seasons" of play, not just the current process's
  runtime.

## 9. Turn/tick model

Text MUD-style games from this era ran on either strict turns or a
background tick clock shared by everyone online. Since v1 has no live
concurrent humans, the simplest faithful-enough model is: **a background tick
(e.g., every 2 real-time seconds) advances Tachyon drain, NPC actions, and store
restocking, while the human player acts asynchronously between ticks** by
typing commands — this reproduces the "the world moves whether you're
typing or not" feel BBS door games had (other users' actions interleaved with
yours) using NPCs instead of real concurrent users.

> **Implementation (2026-09-07).** Until now only the multiplayer server
> actually did this — `ChronoTravelers.Server`'s `PeriodicTimer` ticks
> every `--tick-ms` (default 2000ms) purely on a wall clock, independent
> of whether/when any session types a command. The single-player console
> instead ticked once per typed command and never otherwise — a "turn"
> model, not the async one this section describes: nothing advanced while
> the player sat reading the screen deciding what to do next.
>
> `ChronoTravelers.Console/Program.cs` now runs a background
> `System.Threading.Timer` alongside the existing per-command tick, on the
> same 2-second cadence as the server's default. It's self-resetting, not
> a fixed period: every typed command re-arms it for another 2 seconds, so
> during normal, actively-typed play it never actually fires — it only
> goes off once the player has gone a full 2 seconds without submitting
> anything, which is exactly the gap the old model never advanced through.
> A background-triggered tick always ticks with `playerActedIdly: true`
> (no command happened, so by definition it's an idle beat), and is
> capable of the same ambush death the per-command path always was —
> narration and the death/DeathRecall handling were pulled into one
> shared routine so a monster that finishes the player off while they're
> mid-thought at the prompt gets identical treatment to one that lands the
> blow between two typed commands, rather than that path only existing for
> the command-triggered case.
>
> The two tick sources (main thread, blocked on `Console.ReadLine()`
> between commands; timer thread, firing on its own schedule) share the
> Traveler/world/NPC/simulation state, so a lock guards both — the same
> `_gate`-around-every-mutating-entry-point pattern
> `ChronoTravelers.Game.SharedGame` already uses for the equivalent
> problem on the multiplayer side. The lock is held for a whole typed
> command's processing, not just its tick, so the background timer can
> only ever fire while the player is actually idle at the prompt, never
> mid-command.
>
> One deliberate, player-facing consequence: a background tick's
> narration prints immediately, even while `Console.ReadLine()` is
> blocked mid-prompt waiting on the player — the genuine "something just
> happened without you doing anything" BBS-door feel this section asks
> for, not a bug to design around. The console-only cost is cosmetic: if
> the player is mid-way through typing a line when a tick lands, the
> narration can print through/above their in-progress input. Only the
> narration line(s) print from a background tick, never a full room
> re-render — mirrors how the multiplayer server also only pushes short
> narration to idle sessions each tick rather than re-sending the whole
> room. The timer runs only during the main gameplay loop (started once
> the character/world are ready, disposed on `menu`/`quit`) — not during
> character creation or the buy/sell menus, which are brief blocking
> sub-prompts that don't need it.
>
> Scope call, flagged: no new automated tests cover the timer wiring
> itself. `Program.cs` is top-level statements with no existing seam for
> unit-testing its control flow (no other console behavior — `HandleFight`,
> `HandleMove`, etc. — has test coverage either, for the same reason), and
> extracting it into a testable class was judged out of scope for this
> item. The logic that actually matters for correctness — what a tick
> does, and the "lingered" ambush check `playerActedIdly` feeds into
> (`WorldSimulation.Tick`) — is unchanged and already has full test
> coverage; what's new here is purely *when* that existing, already-tested
> method gets called from.

## 10. UI / presentation

- Pure text, ANSI-style color coding preserved where it aids readability
  (status red, exits green, ambient text default) — matches the one surviving
  screenshot's visual language.
- Windows console/terminal application; a scrollable log pane and a fixed
  status bar (HP/Tachyons/Credits/Level/Location) are a modernization, not a
  historical requirement, and are recommended for playability.

## 11. Explicit non-goals for v1

- Live network multiplayer (real concurrent human players) — architecture
  should not preclude it later, but it is not built now.
- PvP combat between humans — the door game supported it per the "who kills
  who" broadcast messages; v1 can allow player-vs-NPC-Traveler combat (since
  NPCs are full Travelers) which covers the spirit of it without needing a
  netcode layer.

  > **Implementation (2026-09-07).** `fight <name>` (console) now also
  > targets a living NPC Traveler sharing the player's tile when no monster
  > is there to fight instead — checked only as that fallback, so a room
  > with both still defaults to the monster. The fight auto-resolves via a
  > new `ChronoTravelers.Engine.Combat.CombatResolver.FightTraveler`,
  > mirroring the shape every fight already takes on the multiplayer server
  > (`ChronoTravelers.Game.Commands.Fight`) rather than retrofitting the
  > interactive, Monster-coupled `CombatSession` used for console monster
  > fights — no round-by-round "attack or cast" prompts for a PvP bout,
  > just the full log printed at once. `ChronoTravelers.Game.Commands.cs`'s
  > `Fight` gained the identical NPC-targeting fallback for the multiplayer
  > server. Both sides fight on `EffectiveAttackPower`/`EffectiveDefense`
  > alone (gear, buffs, and passives already folded in) — the Monster-only
  > tag-based bonuses in `Traveler.AttackDamageMultiplierAgainst` don't
  > apply to a Traveler opponent, so they're skipped rather than guessed
  > at. On a win, the loser's entire inventory (equipped weapon/armor
  > included) drops to the floor for the winner to `take` — since a
  > defeated NPC is wholesale replaced by the next tick's respawn
  > (`WorldSimulation.RespawnDeadNpcs`), this is the only chance to harvest
  > its gear, otherwise lost for good. XP/Credits reuse
  > `MonsterScaling.KillXp`/`KillCredits`'s existing outlevel-scaled
  > formula, treating the defeated NPC's own level (÷10) as its "tier" —
  > Travelers have no monster tier, but character level is the one
  > apples-to-apples axis both share, and this avoids a second,
  > independently-tuned reward curve. On a loss, the human player gets the
  > same §3.3 death & recall as losing to a monster (no special-casing
  > needed); a losing NPC needs no special handling either — it just sits
  > at 0 HP until the next tick's respawn replaces it, exactly as if a
  > monster (or the world tick itself) had killed it. **Judgment calls,
  > flagged:** basic attacks only, no ability casting, on either side (the
  > same simplification the abstract/NPC-grind `CombatResolver.Fight`
  > already makes); a ranged weapon can't be aimed at an NPC (`fight`'s
  > ranged-lock-on only ever targets a Monster) — PvP is melee-only for
  > now; human-vs-human PvP remains explicitly out of scope, unchanged by
  > this — only player-vs-NPC-Traveler is implemented.

- Mobile/console ports.

## 12. Open design questions for follow-up

- Number and boundaries of the era bands across 2000–5000 (currently 14;
  more, finer bands would give tighter thematic progression).
- Travel throughput: a jump is paid from the instantaneous Tachyon pool.
  Playtest tuning (coefficient 0.2 → 0.04, +1 TachyonsPerLevel across all
  classes, a steeper early tier curve, and — most recently — **removing
  the player's Tachyon pool ceiling** so a big jump is a stockpiling goal
  rather than a hard block) has made travel practical at every range. A
  full cross-timeline leap is still a ~120-Tachyon commitment you build toward
  by converting loot.
  ~~If pacing later feels off, the remaining lever is a "charge a jump over
  several ticks" mechanic.~~ **Implemented** (docs/ENDGAME_STRATEGY.md
  recommendation 5): a jump farther than
  `Traveler.ChargeTravelThresholdYears` (750 years) still costs the same
  Tachyons, spent up front, but doesn't arrive immediately — it "charges"
  for `Traveler.TicksRequiredForChargedTravel(distance)` world ticks (1 at
  the threshold, +1 per additional 250 years, capped at 10) before
  `WorldSimulation`'s per-tick `Traveler.AdvancePendingTravel()` call
  resolves it (`SetCurrentYear`/`PlaceAt`, then a `GameEvent.TimeTraveled`
  broadcast — the same two calls and event a normal jump uses, just
  deferred). Calling `travel` again toward the same target while charging
  is a no-op status query; toward a different target it cancels the old
  charge (no refund — you already committed those Tachyons) and starts the
  new one. `TimeTravelResolver.Travel` returns a `TimeTravelResult` with
  `IsCharging: true` and a `ChargingTargetYear`/`ChargingTicksRequired`
  instead of a `NewYear` while a charge is in flight; the console front
  end (`ChronoTravelers.Console`) prints "Charging a jump to `<year>` A.D.
  — arrival in `<N>` tick(s)." The pending-charge state round-trips
  through a save (`CharacterSaveData.ChargingTargetYear`/
  `ChargingTicksRequired`/`ChargingTicksRemaining`, all nullable — an old
  save with none of them restores to "not charging"). NPCs can never reach
  750 years in one jump (`NpcController.MaxTravelHop` tops out at 300, 450
  for a Wanderer), so charging is reachable only by a player-initiated
  jump; the shared-world server's own `travel` command
  (`ChronoTravelers.Game.Commands.Travel`) resolves its jumps inline
  rather than through `TimeTravelResolver` and was left as-is — a
  follow-up if multiplayer wants the same mechanic.
- ~~Whether NPC store ownership should be capped (to avoid NPCs monopolizing
  all store slots before the human player can buy in).~~ Resolved (§6.2):
  NPCs may buy and run stores, but never the year's last purchasable slot —
  a reservation rule, not an outright ownership count cap.
- Save format: single local save vs. multiple character slots.
