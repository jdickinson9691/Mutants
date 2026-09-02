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
  loot** (`ItemType.Junk`), i.e. +500%. Junk exists only to be burned or
  sold; at the old flat 0.4 it was a poor trickle next to the travel bills a
  downstream push runs up, so clearing the floor after a fight now actually
  refuels you. Still replicates the "quasi semi-flawed but usable" economy
  the original was known for, without the exploitable parts.
- **Tachyon pool size** was scaled up end-to-end for the downstream push: the
  starting pool (`ClassDefinition.BaseTachyons`) tripled (+200%, Soldier
  20→60 … Scientist 34→102) and per-level growth (`TachyonsPerLevel`) ×6
  (+500%, 4→24 for melee, 5→30 for the casters). A thin pool meant a botched
  overreach couldn't afford the retreat home and spiralled into a no-fuel
  death, and the old per-level trickle never re-opened the buffer. A level-10
  Soldier's nominal pool is now 276 (was 56). The pool is uncapped regardless;
  these numbers only set the starting fill and the passive-regen ceiling.
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
  the geometry stored. Room descriptions are drawn from ~15 authored
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
- Soft level cap tied to progress: `character_level ≈ 10 * tier`, where
  `tier` is the scaling tier for the **furthest year the character has
  reached** (`TimeScale.SoftLevelCapForYear`, clamped to 10–60). Keeps
  power and depth loosely paired without hard-blocking grinding.
- **Hard level cap is 60** (`Leveling.MaxCharacterLevel`, raised 30 → 60).
  At the old cap a Traveler stopped growing around year 2500 and rode the
  back 5/6 of the timeline with frozen power; levels 31–60 keep the stat
  and HP/Tachyon curves climbing so year 5000 is a reachable target. The
  **ability trees are unchanged — still 6 tiers, topping out at level 30**
  (`Leveling.TopAbilityLevel`); levels 31–60 grant no new abilities yet
  (see §4.2). The XP curve is quadratic through level 25
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

### 4.2 Ability trees (original design, 6 tiers per class = levels 5/10/15/20/25/30)

> **Level cap 60, ability trees still 6 tiers.** The hard level cap was
> raised to 60 (§4.1) but the ability trees below were not extended — the
> last tier still unlocks at level 30 (Engineer 21). Levels 31–60 are
> stat/HP growth only. Extending the trees with survival-focused deep
> tiers is a planned follow-up ("Reaching Year 5000" design plan, Step 2).

> **Engineer exception:** its tree unlocks on an accelerated schedule —
> **levels 2 / 5 / 9 / 13 / 17 / 21** — because the Engineer is the
> frailest melee class (18 HP, Intellect-primary) and had no answer to a
> bad early fight before its kit came online. Dampener (slow the target so
> you strike first) at level 2 and Sabotage (cut incoming damage) at 9 are
> the survival tools it needs early (playtest feedback). See
> `src/ChronoTravelers.Content/abilities.json`.

Example — **Soldier**:
1. Lv5 — *Suppressing Sweep*: rake fire across the target and up to 2 others crowding it.
2. Lv10 — *Field Patch*: once per fight, a trauma seal for 20% max HP — no Tachyons.
3. Lv15 — *Armor-Piercing Rounds*: sabot loads punch through part of the target's plating.
4. Lv20 — *Fire Discipline*: call the shots — nearby allies hit harder for a while.
5. Lv25 — *Breach Stance*: set behind the shield — incoming damage drops.
6. Lv30 — *Confirmed Kill*: heavy bonus damage vs. targets below 25% HP.

Example — **Doctor**:
1. Lv5 — *Triage*: focused single-target heal (Tachyon cost).
2. Lv10 — *Combat Stim*: your strikes land harder for a while.
3. Lv15 — *Purge Echo*: a resonance burst — devastating to `echo`-tagged monsters.
4. Lv20 — *Broad-Spectrum*: field-treat yourself and every ally in the room.
5. Lv25 — *Crash Cart*: bring a downed ally NPC back on partial vitals (rare, long cooldown).
6. Lv30 — *Iso Field*: a sterile bubble — a brief window of total immunity.

(Spy, Scientist, Engineer get parallel 6-tier trees — full tables live in
`docs/CONTENT_PLAN.md` so this GDD stays a living-but-stable reference; the
pattern — 6 tiers, one per 5 levels, escalating from single-target to
area/group to a capstone — is the standard every class follows.)

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
  chance at a second (~0.35), then a **piece of gear** (weapon / armour /
  ranged, rarity-weighted) at ~0.35, then a **consumable** at ~0.20. Every
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
  (destroy for Tachyons, value per §2.1). `sell`/`convert` work on any item
  regardless of type; `wield` makes sense for Weapon/Armor/Ranged.
- **Ranged weapons** (original addition, enabled by §7.1's spatial
  monsters): wands, bows, and — in later years — guns occupy their own
  equip slot alongside the melee weapon. `wield` one, then `point <dir>`
  (wands) or `shoot <dir>` (bows/guns) fires it down an exit at the first
  monster in the **adjacent** room — an out-of-combat action for softening
  or finishing a target before it reaches you; the shot has no direction
  inside a locked 1v1, so it isn't a combat-round option. Each ranged
  weapon carries a **finite built-in magazine** (no separate ammo item);
  every shot spends one round and the count persists in the save. A wand
  may also carry an effect — `Weaken` leaves the target fighting at
  reduced defence for its next `fight`. Once empty the weapon can't fire
  and is worth only a fraction (down to 25%, scaling with rounds spent) on
  `convert`/`sell`. Damage-wise wands and guns pierce armour; bows don't.
  A kill from range drops the loot on the target's floor — walk in and
  `take` it.
- **Consumables** (original addition — not in the source material) get a
  fourth verb instead of `wield`: `use`/`eat`/`drink <item>`, which
  triggers the item's effect and destroys it — an instant flat HP heal for
  "food," or a temporary attack/defense buff (lasting a set number of
  world ticks) for a "potion." This is separate from — and doesn't cost
  Tachyons like — the `heal` command in §2/§2.1; a potion/food item is a
  one-time consumable you have to loot or buy first.
- Rarity bands (Common/Uncommon/Rare/Epic/Legendary) — original addition,
  since the source material never specifies a rarity system, but a
  near-universal expectation in a modern loot-driven RPG.
  - **For equippables (weapon / armour / ranged), rarity is *derived from
    power*, not authored.** Each archetype carries a `powerMultiplier`
    (~0.5 "crude" → 1.0 "standard" → ~1.8 "fine" → ~2.9 "relic"); it
    scales the per-tier combat baseline (`LootScaling.EquipBonusFor`,
    band ≈ 0.5×–3.5×), and `Rarity.ForPower` names the band. So the
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

### 6.2 Player-owned stores `[SOURCE: players can buy the NPC stores]`
- A Traveler (human or NPC) can purchase an available depot store slot in a
  year for a Credit cost scaled to that year's tier. A human player's owned
  stores persist across sessions (§3.2).
- Once owned, the player stocks it manually (deposit items from inventory,
  set an asking price per item, within store-level-appropriate bounds to
  prevent trivial arbitrage).
- **NPC shoppers** periodically path to player-owned stores and buy/sell
  based on their own needs (an NPC low on a class-appropriate weapon will buy
  one if the store has it and the price is within their budget heuristic; an
  NPC over-encumbered with junk will sell to a store with open capital) —
  this is the "NPCs will sometimes visit and buy and sell from the player
  stores" requirement, made concrete.
- Store owners collect Credits from NPC sales automatically (idle-income
  loop) and can visit in person to restock/collect/adjust prices.

### 6.3 Economy safeguards (original, informed by the source's known flaw)
The original economy was described by a contemporary player as "quasi
semi-flawed." To avoid reproducing that: NPC store customers have a budget
cap per visit, sale prices are clamped to a level-appropriate band (no
selling level-1 junk into a level-10 store for level-10 money), and Credit
sinks exist (store purchase cost, restocking depot inventory, repair costs)
so currency doesn't purely inflate.

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
  trade at a year's store (selling genuine surplus gear it can't use before
  falling back to excess junk, and buying a weapon if unarmed), grind
  monsters in its year, or hop along the timeline. A local-pool NPC not
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
  - `MonsterScaling.BaseAttackPower` is **superlinear**,
    `3 + 2·tier + 0.3·tier²` — near-identical to the old `3 + 2·tier` at
    the low end, ramping hard late (tier 9 ≈ 45 vs the old 21).
  - `MonsterScaling.BaseHp` is **superlinear** too, `20 + 8·tier + tier²`
    (tier 9 ≈ 173 vs the old 92). A level-cap character's attack used to
    one-shot a deep regular monster, so however hard it hit it only ever
    landed one swing; now a far-future fight is a two-plus-round exchange.
  - `CombatResolver.RollDamage` applies an **armour-penetration floor**: a
    hit always lands ≥ 30% of the attacker's power before variance, so
    heavy armour steeply reduces damage but can't zero it. Only bites
    against a well-armoured defender (i.e. the player); monster-vs-monster
    and player-vs-monster are unchanged.
  - **Deep-tier starter weapons** (`TimelineContentFactory`,
    `MaybeStarterWeapon`): from tier 4 up, a monster has a rising chance
    (~15% → ~85%) of spawning already wielding a modest weapon — adds to
    its `EffectiveAttackPower`, drops as loot on death, deterministic per
    species/year.
  - Net: an armed tier-9 regular is a ~2-round fight that costs ~45% of a
    level-appropriate HP pool; unarmed ~18%. Apex / Warden HP scale with
    the new curve (tier-9 apex ≈ 415, Warden ≈ 520) into longer fights.
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
  cap (~a third of its rooms) without ever overflowing.
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
- Mobile/console ports.

## 12. Open design questions for follow-up

- Number and boundaries of the era bands across 2000–5000 (currently ~15;
  more, finer bands would give tighter thematic progression).
- Travel throughput: a jump is paid from the instantaneous Tachyon pool.
  Playtest tuning (coefficient 0.2 → 0.04, +1 TachyonsPerLevel across all
  classes, a steeper early tier curve, and — most recently — **removing
  the player's Tachyon pool ceiling** so a big jump is a stockpiling goal
  rather than a hard block) has made travel practical at every range. A
  full cross-timeline leap is still a ~120-Tachyon commitment you build toward
  by converting loot. If pacing later feels off, the remaining lever is a
  "charge a jump over several ticks" mechanic.
- Whether NPC store ownership should be capped (to avoid NPCs monopolizing
  all store slots before the human player can buy in).
- Save format: single local save vs. multiple character slots.
