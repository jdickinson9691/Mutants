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

You are a Mutant in the year 2000 A.D., clawing your way to being the most
powerful, richest being on the planet `[SOURCE]`. You explore a grid-based
city and wasteland, fight monsters for loot, convert junk into Ions to
survive and travel, buy and run stores, and burn Ions to leap between
increasingly dangerous "time travel levels." Every other Mutant you meet in
the world — friendly, hostile, or running a shop — is an NPC governed by the
same rules you are.

## 2. Core resource: Ions `[SOURCE]`

Ions are the single unified resource for:
- **Survival** — passive drain per turn/tick; hitting 0 starts costing HP.
- **Healing** — spend Ions to heal wounds directly.
- **Spellcasting** — arcane/divine classes spend Ions per ability.
- **Time travel** — spend a (level-dependent) lump of Ions to travel to another
  time-travel level.

Ions are generated almost entirely by **converting items** — `convert <item>`
destroys the item and adds Ions based on the item's tier/value `[SOURCE
mechanic, original value curve]`. This makes every piece of loot a three-way
choice, mirrored exactly from the source game: **wield it, sell it, or burn
it** `[SOURCE]`.

### 2.1 Ion economy tuning (original)
- Passive drain: 1 Ion per N game-ticks, scaled slightly up at deeper time
  levels (deeper levels are harsher survival environments).
- Item→Ion conversion value = `base_item_value * 0.4`, rounded down, with a
  minimum of 1. This keeps converting strictly worse than selling in Riblets
  when a store is reachable, but better than nothing when it isn't — replicating
  the "quasi semi-flawed but usable" economy the original was known for,
  without the parts that made it exploitable.
- Time travel cost = `25 * target_level` Ions (see §4).

## 3. Movement & the world map

### 3.1 Grid and compass `[SOURCE]`
Each time-travel level is a 2D grid of rooms addressed by East/West and
North/South offsets from a level's origin, exactly matching the surviving
screenshot's `Compass: (2E : 0N)` readout. Movement commands are single-letter
directions: `n`, `s`, `e`, `w` (and optionally `ne/nw/se/sw` as a v1.1
stretch goal, not in the original).

- Rooms have short, atmospheric one-line descriptions (`You're in a
  maintenance shop.`, `You see rubble everywhere.`, `You feel a cold
  breeze.`) `[SOURCE style]`.
- Adjacent-room hints are surfaced before you move into them (`You see
  shadows to the east, west.`) `[SOURCE style]`.
- Available exits are always listed explicitly, e.g. `north - area
  continues.` `[SOURCE style]`.

### 3.2 Levels & time travel `[SOURCE: confirmed mechanic + Ion cost]`
- The world is organized into a sequence of **time-travel levels**, each a
  separate grid map with its own room content, monster roster, loot tables,
  and store population. Level 1 is the "present" city; each level deeper is a
  different era (a specific theme-per-level list is content work, not
  mechanics — see `docs/CONTENT_PLAN.md` once populated).
- Command: `travel <level>` (only to levels you've unlocked — see below) or
  `travel next` / `travel prev` for adjacent levels.
- Cost: `25 * target_level` Ions, deducted on success. Insufficient Ions
  produces a warning and blocks the jump — this exact failure mode is
  independently confirmed by a historical MBBSEmu bug report about a "warning
  when attempting to time travel without enough ions."
- **Unlocking** a new level requires reaching a minimum character level *and*
  defeating that level's "gatekeeper" monster once (original design, gives
  loot-and-level scaling somewhere to anchor on).
- Levels only go one direction deeper until a level cap; a `travel prev`
  always works for free (no cost to retreat to a shallower, already-unlocked
  level) so lower-level content stays reachable for restocking/trading.

### 3.3 Death & recall
- Dying drops a portion of unconverted inventory at the death location (loot
  becomes lootable by other NPCs/players) and returns the character to their
  level's home base with an Ion penalty. No source material describes death
  handling, so this is original, tuned to punish but not erase progress.

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
- Level cap per time-travel level context: a soft cap exists at
  `character_level = 10 * unlocked_time_level` to keep power and depth loosely
  paired without hard-blocking grinding.
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

- **Sources of loot**: monster drops on defeat (weighted table per monster,
  scaled to the time-travel level it lives on) and **random location spawns**
  (a periodic chance per room, per tick, for an item to appear on the ground —
  matches the brief "random chance of spawning in a location" requirement;
  original spawn-rate tuning).
- **Scaling**: every item has a `tier` equal to the time-travel level it was
  generated on (or dropped by a monster native to that level); tier drives
  base stats, sell price, and Ion-conversion value, so loot from level 5 is
  categorically better than loot from level 1 — this directly implements the
  "loot scales based on time travel level" requirement.
- **Disposition**: every lootable item supports the same three verbs as the
  original — `wield` (equip if class-compatible), `sell <item>` (at any
  store, price is store-and-negotiation-dependent, see §6), `convert <item>`
  (destroy for Ions, value per §2.1).
- Rarity bands (Common/Uncommon/Rare/Epic/Legendary) modulate stat rolls
  within a tier — original addition, since the source material never
  specifies a rarity system, but is a near-universal expectation in a modern
  loot-driven RPG and doesn't conflict with anything documented.

## 6. Stores & economy `[SOURCE: purchasable NPC-run stores + Riblets]`

### 6.1 NPC government stores
- The in-fiction government continuously seeds new stores into each level's
  city area `[SOURCE]`. These are the baseline places to sell loot for
  **Riblets** and buy consumables/basic gear.
- Prices scale with the store's home time-travel level (a level-5 store deals
  in level-5-tier goods and pays/charges accordingly) — this is how "an
  economy based on the time travel level" gets implemented concretely.

### 6.2 Player-owned stores `[SOURCE: players can buy government stores]`
- A player (human or NPC) can purchase an available government-built store
  slot in a level's city for a Riblet cost scaled to that level.
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

- A configurable population of NPC "Mutants" exists per time-travel level,
  each a full character with class, level, inventory, and Ion pool — built
  on the *exact same character/inventory/ability code path* as the human
  player, per the requirement that NPCs "play like players, with the same
  character classes and restrictions."
- Each NPC runs a lightweight behavior loop each tick: assess Ion level (seek
  conversion fodder or a store if low), assess HP (retreat/heal if low),
  otherwise pursue its current goal (grind monsters near its level, path to
  a store to trade, attempt a time-travel jump if it has both the Ions and
  the level-unlock, occasionally visit/stock a store it owns).
- NPCs participate in the same kill-feed / **telepathic broadcast** channel
  as the player `[SOURCE]` — "X was slain by Y," "Z reached level N," "W time
  traveled to level 5" — so the leaderboard and the "who's doing what" feel of
  the original survives without a live human population.
- NPC decision-making is intentionally simple and rule-based for v1 (finite
  state machine, not full pathfinding AI/ML) to keep it debuggable and cheap
  to simulate at scale; an upgrade path to smarter behavior is a documented
  v2 idea, not a v1 requirement.

## 8. Leaderboards `[SOURCE: MutantLink cross-board high scores]`

Displayed on the game's start/title screen, refreshed each session:
- **Deepest Time Travel Level Reached** (all-time, across player + NPCs).
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

- Exact number of time-travel levels for v1 launch content (recommend
  starting with 5–8 fully realized levels rather than many shallow ones).
- Whether NPC store ownership should be capped (to avoid NPCs monopolizing
  all store slots before the human player can buy in).
- Save format: single local save vs. multiple character slots.
