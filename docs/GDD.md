# Game Design Document — "Chronomutants" (working title)

A standalone, single-player-capable, text-based RPG inspired by the Major BBS
door game *Mutants!*. Version 1 simulates all other "players" as NPCs with the
same classes, rules, and restrictions as the human player, so the world feels
populated even with nobody else connected. Multiplayer/network play is an
explicit non-goal for v1 and a likely v2+ direction (see §11).

Everything here is original design **except** where marked `[SOURCE]`, which
means it's a confirmed mechanic from `research/ORIGINAL_MUTANTS_RESEARCH.md`.
Anything not marked that way fills a documented gap in the historical record.

---

## 1. High concept

You are a Mutant starting in the year 2000 A.D., clawing your way to being
the most powerful, richest being on the planet `[SOURCE]`. You explore a
grid-based city and wasteland, fight monsters for loot, convert junk into
Ions to survive and travel, buy and run stores, and burn Ions to `travel`
anywhere on the 2000–5000 A.D. timeline — later years are richer and far
more dangerous. Every other Mutant you meet in the world — friendly,
hostile, or running a shop — is an NPC governed by the same rules you are.

## 2. Core resource: Ions `[SOURCE]`

Ions are the single unified resource for:
- **Survival** — passive drain per turn/tick; hitting 0 starts costing HP.
- **Healing** — the `heal` command spends Ions to heal wounds directly,
  usable at any time (no location or combat requirement) and, like every
  other action, advances one tick `[SOURCE]`.
- **Spellcasting** — arcane/divine classes spend Ions per ability.
- **Time travel** — spend Ions proportional to how many years you jump
  (see §3.2) to move anywhere on the 2000–5000 A.D. timeline.

Ions are generated almost entirely by **converting items** — `convert <item>`
destroys the item and adds Ions based on the item's tier/value `[SOURCE
mechanic, original value curve]`. This makes every piece of loot a three-way
choice, mirrored exactly from the source game: **wield it, sell it, or burn
it** `[SOURCE]`.

### 2.1 Ion economy tuning (original)
- Passive drain: 1 Ion per N game-ticks, scaled slightly up further into
  the future (later years are harsher survival environments) — the scaling
  key is the whole-number difficulty tier for the current year (see §3.2).
- Passive **regen**: 1 Ion per M game-ticks out of combat, faster than the
  drain in early years and slower in the far future. Net effect: the
  present is survivable (grind → heal → recover), the deep future
  net-drains you. Added after playtesting showed the drain-only model made
  the early game an unrecoverable attrition spiral.
- Item→Ion conversion value = `base_item_value * 0.4`, rounded down, with a
  minimum of 1. This keeps converting strictly worse than selling in Riblets
  when a store is reachable, but better than nothing when it isn't — replicating
  the "quasi semi-flawed but usable" economy the original was known for,
  without the parts that made it exploitable.
- Time travel cost = `ceil(0.04 * |target_year - current_year|)` Ions,
  minimum 1 for any real jump, symmetric (retreating toward the present
  costs the same as advancing). Original tuning (0.2 → 0.1 → 0.04 across
  playtests). The Ion **pool cap** still bounds a single jump's distance,
  but at 0.04 a one-tier early hop (~250 yrs) is affordable from level 1;
  only a full cross-timeline leap is a late-game commitment.
- `heal` restores HP at 3 HP per 1 Ion — no ratio survives in the
  historical record; 3:1 keeps healing a real competitor for the Ion pool
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

### 3.2 The timeline & time travel `[SOURCE: confirmed mechanic + Ion cost]`
- The world is a **continuous timeline** from year **2000 A.D.** (the
  "present" city, where every character starts) to **5000 A.D.**. There are
  no discrete levels: difficulty, monster stats, and loot value all scale
  smoothly with the year. Year 2000 sits at scaling "tier" 1.0 and year
  5000 at tier 9.0, on a **piecewise curve that's steeper early** — one
  tier per 250 years through 2000–3000 (reaching tier 5), then one per 500
  years after — so a short early hop actually changes the fight instead of
  the first ~600 years all playing the same (`Mutants.Core.Time.TimeScale`).
- **Each year has its own grid map**, generated deterministically from a
  per-save **world seed** plus the year — the same year always produces the
  same layout, so it can be a pure function of the save with nothing about
  the geometry stored. Room descriptions are drawn from ~15 authored
  **era bands** (Ruined City → … → The Final Instant) that tile the 3000
  years; a year takes its theme and monster/loot pools from its band.
- Command: `travel <year>` (any year 2000–5000), `travel +N` / `travel -N`
  (relative), or `travel next` / `travel prev` (the next/previous
  Gatekeeper year).
- Cost: `ceil(0.04 * |target_year - current_year|)` Ions, minimum 1,
  deducted on success (coefficient lowered 0.2 → 0.1 → 0.04 across
  playtests so mid-range hops are affordable from low level — a one-tier
  early jump is ~10 Ions, a full cross-timeline leap still ~120).
  Symmetric — retreating toward the present costs the same as advancing
  (this supersedes the earlier "retreat is free" rule now that travel is
  otherwise unrestricted). Insufficient Ions produces a warning and blocks
  the jump — the failure mode independently confirmed by a historical
  MBBSEmu bug report about a "warning when attempting to time travel
  without enough ions."
- **Travel is otherwise unrestricted**: no unlock, no minimum character
  level, no gate. How hard the fights get is the only limiter — and
  **overreaching is a deliberate option**: jumping well past your level
  band drops you among monsters (and loot, and gear) scaled far above you,
  a high-risk raid for a shot at better equipment. The console flags such
  a jump ("2450 A.D. is around tier 3 — scales to ~level 30, you're level
  1") and asks to confirm, but never forbids it.
- **Gatekeepers** are still here, but as tough optional encounters rather
  than gates. The world seed places one every random 50–100 years across
  the timeline; a Gatekeeper year holds a bullet-sponge boss (~3× a regular
  monster's HP for that year) guarding a guaranteed year-scaled **Legendary
  weapon trophy**, present until you beat it once. It blocks nothing —
  travelling past a Gatekeeper year was never restricted.
- **Persistence**: map layouts are regenerated from the seed, not stored.
  What the save keeps per character is the world seed, the current and
  furthest-reached year, the set of cleared Gatekeeper years, and every
  store the player owns (which year, its Riblet capital, and its
  listings — re-attached to the regenerated world on load).

### 3.3 Death & recall
- Dying drops a portion of unconverted inventory at the death location (loot
  becomes lootable by other NPCs/players) and returns the character to the
  present with an Ion penalty. No source material describes death handling,
  so this is original, tuned to punish but not erase progress.

## 4. Character classes

`[SOURCE: five classes confirmed — Thief, Priest, Wizard, Warrior, Mage,
per the MBBSEmu wiki; "Barbarian/Cleric" appear in a second, looser source
and are treated as the same archetypes under different version-era names.]`
All ability names, numbers, and level-gates below are original design filling
a documented gap.

Every class shares: HP, Ions, a primary attack, an inventory, and access to
`convert`/`wield`/`sell`. Classes differ in HP/Ion scaling, their unlocked
ability tree, and which loot they can equip.

| Class | Role | Primary stat | Flavor |
|---|---|---|---|
| Warrior | Melee tank/damage | Strength | Front-line brawler, best HP, heaviest weapons/armor |
| Thief | Skirmisher/utility | Agility | Stealth, critical strikes, lockpicking/store discounts |
| Priest | Support/healer | Faith | Group heals, buffs, undead-effective damage |
| Mage | Arcane blaster | Intellect | High burst Ion-cost spells, area damage, weak melee |
| Wizard* | Arcane utility | Intellect | Control/debuff spells, teleport-lite movement tricks |

\* Kept as a distinct 5th class (rather than merging with Mage) to honor the
wiki's explicit 5-name list; differentiated by role (control/utility vs.
blaster) so the two arcane classes don't overlap mechanically.

### 4.1 Leveling
- XP from monster kills, scaled by monster level relative to the killer.
- Soft level cap tied to progress: `character_level ≈ 10 * tier`, where
  `tier` is the scaling tier for the **furthest year the character has
  reached** (`TimeScale.SoftLevelCapForYear`, clamped to 10–30). Keeps
  power and depth loosely paired without hard-blocking grinding.
- Every level grants a stat increase; every **5th level** grants a new class
  ability (see §4.2), rewarding both steady growth and periodic power spikes.

### 4.2 Ability trees (original design, 6 tiers per class = levels 5/10/15/20/25/30)

Example — **Warrior**:
1. Lv5 — *Cleave*: hit up to 2 additional adjacent enemies.
2. Lv10 — *Second Wind*: once per fight, heal 20% max HP for free (no Ions).
3. Lv15 — *Guard Break*: bypass a portion of target's armor.
4. Lv20 — *Rally*: nearby NPC allies gain a temporary damage buff.
5. Lv25 — *Juggernaut*: temporary damage reduction stance.
6. Lv30 — *Executioner*: bonus damage vs. targets below 25% HP.

Example — **Priest**:
1. Lv5 — *Mend*: single-target heal (Ion cost).
2. Lv10 — *Bless*: temporary to-hit/damage buff for self or target.
3. Lv15 — *Turn Undead*: strong bonus damage + fear vs. undead monster tag.
4. Lv20 — *Circle Heal*: heal self + adjacent party/NPC allies.
5. Lv25 — *Resurrect Lite*: revive a fallen ally NPC with partial HP (rare, long cooldown).
6. Lv30 — *Sanctuary*: brief immunity window.

(Thief, Mage, Wizard get parallel 6-tier trees — full tables live in
`docs/CONTENT_PLAN.md` so this GDD stays a living-but-stable reference; the
pattern — 6 tiers, one per 5 levels, escalating from single-target to
area/group to a capstone — is the standard every class follows.)

### 4.3 Restrictions (apply identically to player and NPCs)
- Weapon/armor equip requirescl ass-tagged gear (a Mage can't wield the
  Warrior's two-handed axe at full effectiveness — non-class gear works at a
  penalty rather than being hard-blocked, to keep loot from feeling wasted).
- Ion pools and drain rates differ per class (Mage/Wizard drain faster from
  spell use; Warrior/Thief drain slowest, lean harder on raw HP).

## 5. Loot system `[SOURCE: wield/sell/convert]`

- **Sources of loot**: monster drops on defeat (a themed table per
  monster, built from item archetypes and scaled to the **year** it's
  fought in) and **random location spawns** (a periodic chance per room,
  per tick, for an item to appear on the ground — matches the brief "random
  chance of spawning in a location" requirement; original spawn-rate
  tuning).
- **Drop composition**: a regular monster's table is built by category so
  a kill reliably pays and occasionally supplies you — **sell/convert
  fodder** (a junk item) at the highest chance (~0.75), then a **piece of
  gear** (weapon / armour / ranged, rarity-weighted) at ~0.35, then a
  **consumable** at ~0.20. If a species' theme pool lacks a category the
  world generator borrows the cheapest one from the full catalogue, so
  every monster can drop something to sell, something to wear, and
  something to use.
- **Scaling**: an item's `tier` is derived from the **year** it was
  generated in (`TimeScale.TierForYear`, a continuous 1.0–9.0 across
  2000–5000); tier drives base stats, sell price, and Ion-conversion value
  via `LootScaling` (value baseline `12 * tier + 10` — the flat term is a
  playtest bump that roughly doubles tier-1 loot so an early grind funds a
  real purchase, tapering to ~1.3× by tier 9), so loot from year 4000 is
  categorically better than loot from year 2100 — this implements "loot
  scales based on time travel level" against the continuous timeline.
- **Disposition**: every lootable item supports the same three verbs as the
  original — `wield` (equip if class-compatible), `sell <item>` (at any
  store, price is store-and-negotiation-dependent, see §6), `convert <item>`
  (destroy for Ions, value per §2.1). `sell`/`convert` work on any item
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
  Ions like — the `heal` command in §2/§2.1; a potion/food item is a
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
    Gatekeeper's guaranteed trophy sits deep in the Legendary band.
  - Consumables and junk keep an authored rarity (a potion's strength
    isn't a damage number).

## 6. Stores & economy `[SOURCE: purchasable NPC-run stores + Riblets]`

### 6.1 NPC government stores
- Every year has a government store, placed deterministically by the world
  seed `[SOURCE]`. These are the baseline places to sell loot for
  **Riblets** and buy consumables/basic gear; each stocks the same staple
  kinds (a heal item, an attack potion, a defense potion, a weapon, an
  armour piece), pulled from the year's era themes.
- Prices scale with the **year** (a year-4000 store deals in year-4000-tier
  goods and pays/charges accordingly) — this is how "an economy based on
  the time travel level" gets implemented against the timeline.

### 6.2 Player-owned stores `[SOURCE: players can buy government stores]`
- A player (human or NPC) can purchase an available government-built store
  slot in a year for a Riblet cost scaled to that year's tier. A human
  player's owned stores persist across sessions (§3.2).
- Once owned, the player stocks it manually (deposit items from inventory,
  set an asking price per item, within store-level-appropriate bounds to
  prevent trivial arbitrage).
- **NPC shoppers** periodically path to player-owned stores and buy/sell
  based on their own needs (an NPC low on a class-appropriate weapon will buy
  one if the store has it and the price is within their budget heuristic; an
  NPC over-encumbered with junk will sell to a store with open capital) —
  this is the "NPCs will sometimes visit and buy and sell from the player
  stores" requirement, made concrete.
- Store owners collect Riblets from NPC sales automatically (idle-income
  loop) and can visit in person to restock/collect/adjust prices.

### 6.3 Economy safeguards (original, informed by the source's known flaw)
The original economy was described by a contemporary player as "quasi
semi-flawed." To avoid reproducing that: NPC store customers have a budget
cap per visit, sale prices are clamped to a level-appropriate band (no
selling level-1 junk into a level-10 store for level-10 money), and Riblet
sinks exist (store purchase cost, restocking government store inventory,
repair costs) so currency doesn't purely inflate.

## 7. NPC simulation ("simulated players")

Since v1 has no network multiplayer, the world needs to feel alive:

- A configurable population of NPC "Mutants" is scattered across the whole
  timeline (a single `totalCount`, each spawned in a random year and
  fast-levelled into that year's soft-cap band), each a full character with
  class, level, inventory, and Ion pool — built on the *exact same
  character/inventory/ability code path* as the human player, per the
  requirement that NPCs "play like players, with the same character classes
  and restrictions."
- Each NPC runs a lightweight behavior loop each tick: assess Ion level (seek
  conversion fodder or a store if low), assess HP (retreat/heal if low),
  otherwise pursue its current goal (grind monsters in its year, trade at
  its year's store, hop a short way along the timeline — usually forward —
  if it can afford the Ion cost).
- NPCs participate in the same kill-feed / **telepathic broadcast** channel
  as the player `[SOURCE]` — "X was slain by Y," "Z reached level N," "W time
  traveled to 3200 A.D." — so the leaderboard and the "who's doing what"
  feel of the original survives without a live human population.
- NPC decision-making is intentionally simple and rule-based for v1 (finite
  state machine, not full pathfinding AI/ML) to keep it debuggable and cheap
  to simulate at scale; an upgrade path to smarter behavior is a documented
  v2 idea, not a v1 requirement.

### 7.1 Spatial monsters
The year the player is standing in also runs a live monster population
(`Mutants.Core.Time.YearPopulation`, seeded deterministically from the
world seed on first entry, kept alive in the session's year memo — not
saved):
- Monsters occupy specific rooms and, each tick, **patrol** through exits,
  **grab loot** off their room's floor, or — if hurt — **heal** from their
  own Ion pool, first **converting** a scavenged item if they're out of
  Ions (the same `heal` / `convert` the player uses). A roaming monster
  keeps **heading the same direction** most turns rather than random-
  walking (with the odd short pause), so it covers ground and its path is
  legible: the `monsters` list shows each one's heading, and you can read
  where it's going and cut it off.
- **Movement is narrated** relative to you, in the source game's style —
  a monster **first coming within one room** ("you hear something to the
  north," with varied phrasing), **entering** your room ("a Rubble Brute
  comes in from the south"), or **leaving** it ("the Alley Runner slips
  away east"). `look` also lists what's stirring in each adjacent room.
- Monsters do **not** automatically pursue or attack anyone who walks
  past. Each carries an **earned aggro meter** toward the player
  (`Mutants.Core.Monsters.AggroModel`), raised by:
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
- Only the player's current year is simulated; every other year holds its
  monsters frozen where they were placed until visited. NPC-Mutants still
  grind abstractly against their year's roster (spatial NPC↔monster
  interaction is a follow-up).

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
(e.g., every 2 real-time seconds) advances Ion drain, NPC actions, and store
restocking, while the human player acts asynchronously between ticks** by
typing commands — this reproduces the "the world moves whether you're
typing or not" feel BBS door games had (other users' actions interleaved with
yours) using NPCs instead of real concurrent users.

## 10. UI / presentation

- Pure text, ANSI-style color coding preserved where it aids readability
  (status red, exits green, ambient text default) — matches the one surviving
  screenshot's visual language.
- Windows console/terminal application; a scrollable log pane and a fixed
  status bar (HP/Ions/Riblets/Level/Location) are a modernization, not a
  historical requirement, and are recommended for playability.

## 11. Explicit non-goals for v1

- Live network multiplayer (real concurrent human players) — architecture
  should not preclude it later, but it is not built now.
- PvP combat between humans — original game supported it per the "who kills
  who" telepathic messages; v1 can allow player-vs-NPC-Mutant combat (since
  NPCs are full Mutants) which covers the spirit of it without needing a
  netcode layer.
- Mobile/console ports.

## 12. Open design questions for follow-up

- Number and boundaries of the era bands across 2000–5000 (currently ~15;
  more, finer bands would give tighter thematic progression).
- Travel throughput vs. Ion-pool cap: a jump is paid from the
  instantaneous Ion pool. Playtest tuning (coefficient 0.2 → 0.04,
  +1 IonsPerLevel across all classes, and a steeper early tier curve so
  affordable hops land in a meaningfully harder year) has made mid-range
  travel practical from low level; a full cross-timeline leap is still a
  deliberate ~120-Ion end-game commitment. If even that later feels wrong,
  the remaining lever is a "charge a jump over several ticks" mechanic.
- Whether NPC store ownership should be capped (to avoid NPCs monopolizing
  all store slots before the human player can buy in).
- Save format: single local save vs. multiple character slots.
