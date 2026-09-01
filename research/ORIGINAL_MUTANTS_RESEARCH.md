# Research: The Original "Mutants!" BBS Door Game

This document collects everything findable on the public web about the original
**Mutants!** door game for The Major BBS / WorldGroup, as of August 2026. The game is
extremely obscure by modern standards — it predates the mainstream web, its
publisher no longer exists in recognizable form, and no full manual, source code, or
complete gameplay transcript is publicly archived. What follows is every verifiable
fact found, each tied to its source, plus notes on where the record runs out and
we have to make a design decision rather than cite history.

## 1. Publisher / history

- Written in ANSI C for **The Major BBS** (Galacticomm's BBS platform), later ported
  to **WorldGroup** (Galacticomm's Windows-era successor product).
- Originally developed by a Canadian company, **MajorSoft**.
- A dispute between MajorSoft's owners reportedly led to a spinoff company,
  **Majorware**, with both companies selling competing versions of the game and
  undercutting each other on price for a time. Majorware apparently retained the
  source code and ultimately became the surviving publisher, releasing newer
  versions of *Mutants!*.
- The internal module name inside The Major BBS Emulation Project is **MJWMUT**;
  the "Original ISV" (independent software vendor) is credited as **Majorware /
  Majorsoft**.
- Listed retail price: **$389.00 USD**.
- Claimed install base: running on **over 500 MajorBBS systems** at its peak, with
  a lifetime reach of **over 20,000 players worldwide**.
- A companion product, **MutantLink**, let BBS operators link high-score tables
  across boards; the linking was reportedly exploitable, and cheating accusations
  were a recurring source of "flame wars" on the Majornet BBS-operator forums.
- The game had a reputation for **crashing the host BBS** on a regular basis —
  stability was a known, long-running issue (echoed in modern restoration efforts
  too — see below).
- *Mutants!* is credited with inspiring competitors, most notably **MajorMUD**,
  one of the best-known BBS door RPGs of the era.
- A later build, **Mutants V4** for WorldGroup 3 (WG3), exists and was still being
  informally run and patched by BBS hobbyists as recently as 2020–2025 (see forum
  thread below). As of a January 2023 post, a member of the modern-day "Major BBS
  Restoration Project" / Elwynor Technologies (current holder of the Galacticomm
  IP) confirmed they possess **the source code, but not the copyright**, to
  Mutants, and intended to port it to later MBBS builds.
- **Versions found:** v3.21b (packaged and emulator-verified by the MBBSEmu
  project) and V4 (WG3-era, referenced only in forum posts, no packaged download
  located).

Sources:
- [Mutants! (MJWMUT) — The MajorBBS Emulation Project Wiki](https://wiki.mbbsemu.com/doku.php?id=modules:mjwmut)
- [Mutants! v3.21b — MBBSEmu module page](https://www.mbbsemu.com/Module/MJWMUT)
- [Mutants! — Everything2.com](https://everything2.com/title/Mutants!)
- [Mutants V4 — The Major BBS Forums](https://www.themajorbbs.com/forums/viewtopic.php?t=49)
- [MBBSEmu GitHub issue #94 — time travel / tachyons warning](https://github.com/enusbaum/MBBSEmu/issues/94)

## 2. Setting / premise

- In-game year: **2000 A.D.** (a "future" setting from the game's early-1990s
  vantage point).
- Premise: the world has been "reformed" after some unspecified catastrophe, and a
  new government has been established. That government "believes strongly in
  trading" and continuously builds new stores in the city, which players are able
  to buy and run themselves.
- Player goal, per the official description: *"to be the most powerful, richest
  being on the planet."*
- The Everything2 account additionally describes a final superboss, **Satan-1**,
  as the ultimate monster to be dueled — this is the only source that mentions a
  named final boss, so treat it as likely-true color rather than confirmed canon.

## 3. Character classes

Two sources give two overlapping-but-not-identical class lists, most likely
reflecting different versions of the game (v3 vs. V4) or the fallibility of a
20+ year old memory:

| Source | Classes listed |
|---|---|
| MBBSEmu wiki (official-sounding, matches installer text) | **Thief, Priest, Wizard, Warrior, Mage** |
| Everything2 (player recollection, 2002) | **Barbarian, Warrior, Thief, Wizard, Cleric** |

Five classes either way. "Priest" and "Cleric" are almost certainly the same
class under different naming across versions; "Mage" and "Wizard" appearing
together in one list but not the other suggests a possible transcription slip
(likely the same arcane-caster class). No source enumerates specific abilities,
stat spreads, or per-level unlocks for any class — that layer of detail was
never published anywhere we could find and does not survive in any archived
manual, so it is **original design work** in the GDD, built to fit the
archetypes actually named (a physical striker/thief-type, a divine
healer/support, an arcane blaster/utility caster, a martial tank/warrior, and a
hybrid/generalist).

## 4. Core survival resource: Tachyons

- The game's core resource is called **tachyons**, described as *"the equivalent to
  food."* Required to survive and to heal wounds.
- Tachyons are generated by **converting** any item in the game: `convert skull`
  is the example command given, which destroys the item in a flash of light and
  raises the player's tachyon level.
- Healing is a dedicated **`heal`** command, usable at any time (not gated
  to combat or a location) to recover an amount of hit points; like any
  other player action, issuing it advances one game tick (per the user's
  own recollection of the original game).
- A modern hobbyist forum post (2020, Mutants V4 on WG3) uses the word
  **"energy"** interchangeably with tachyons for the same converted-item resource,
  confirming the terminology persisted (or was renamed to "energy") into the V4
  build: *"In order to play the game items are converted into energy."*
- A MBBSEmu GitHub issue (#94) is titled *"printf Warning when attempting to
  time travel without enough tachyons,"* which independently confirms that **tachyons
  are the currency spent to time-travel between levels**, not just a
  food/healing resource.
- Tachyons are also stated to power spellcasting and "other special characteristics
  of the player" (per the Everything2 summary) — i.e., tachyons are a unified
  mana/stamina/hunger/travel-fuel resource, not four separate systems.

## 5. Economy / stores

- Money is called **Riblets**.
- Items found or looted can be: **wielded** as a weapon, **sold to a store**
  for Riblets, or **converted** to tachyons.
- The government "constantly builds new stores in the city," and — critically —
  **players can purchase these stores and run their own trading shops**. This is
  a core, confirmed mechanic, not a guess.
- The economy is explicitly called "quasi semi-flawed" by a contemporary player
  account, implying it was exploitable/unbalanced in the original (a cautionary
  note for our own design, not a feature to faithfully reproduce).

## 6. World navigation

- The one surviving gameplay screenshot (from the MBBSEmu wiki) shows the
  actual UI conventions:
  - A compass/coordinate readout in the form `Compass: (2E : 0N)`, i.e. the
    world is a **2D grid of rooms addressed by East/West and North/South
    offset**, MUD-style.
  - Room descriptions are short, flavorful one-liners: *"You're in a
    maintenance shop."*, *"You see rubble everywhere."*, *"You feel a cold
    breeze."*
  - Available exits are listed explicitly per room, e.g. `north - area
    continues.` / `south - area continues.` / `east - area continues.` /
    `west - area continues.`
  - Ambient/adjacent-room hints are shown, e.g. *"You see shadows to the
    east, west."*
  - Movement commands are the expected single-letter directions (`n`, `s`,
    `e`, `w` — the screenshot shows the player typing `n` and `>` as the
    input prompt).
- **Time travel** is the mechanic that moves the player between distinct
  "levels" (temporal eras, each presumably re-using the room/grid engine with
  different content, difficulty, and loot tables). Time travel costs tachyons
  (confirmed by MBBSEmu issue #94); no source specifies the exact tachyon cost
  curve, how many levels existed, or whether travel was one-directional,
  so the specific curve is original design.

## 7. Communication / social systems

- **Telepathic messages** are broadcast to all connected players during the
  game, announcing major events — the example given is *"who kills who."*
  This is effectively a global event/kill-feed channel, BBS-appropriate since
  many players were online concurrently sharing the same door-game world.

## 8. What is *not* documented anywhere found

No public source — including GitHub issues for the MBBSEmu emulator, the
BBS-door-game wikis (Break Into Chat, Telnet BBS Guide), Wikipedia's door-game
list/category, or general BBS retrospectives (e.g. Arcadia BBS's "10 Most
Popular BBS Door Games") — documents:

- Exact leveling curve, XP formula, or level cap
- Per-class ability lists or spell lists
- Monster roster, stats, or AI behavior
- Specific loot tables or item rarity tiers
- Number of time-travel levels or their names/themes
- Leaderboard categories as displayed in-game
- PvP rules (only "who kills who" broadcasts confirm PvP existed at all)

These gaps are exactly where the accompanying Game Design Document makes
original decisions, flagged inline as "new design" rather than "faithful
restoration."

## 9. Search / access notes for future sessions

- No manual, `.doc`/`.txt` files, or NFO were found on textfiles.com or
  elsewhere; the game predates widespread door-game archiving and its ANSI-C
  source was proprietary and disputed between two companies, which likely
  suppressed public documentation even at the time.
- `mbbsemu.com`'s module detail pages are a client-rendered SPA; a plain
  fetch returns an empty shell — a real browser is required to read them
  (used here via the in-app browser).
- The MajorBBS Forums thread on "Mutants V4" (register: `themajorbbs.com`) is
  the single best living lead for anyone who later wants to try to obtain a
  legally-gray copy of the actual V4 source for historical reference; as of
  the last post (Dec 2025) no public release has happened.
