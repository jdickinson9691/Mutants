using ChronTravelers.Core.Characters;
using ChronTravelers.Core.Events;
using ChronTravelers.Core.Ions;
using ChronTravelers.Core.Items;
using ChronTravelers.Core.Monsters;
using ChronTravelers.Core.Time;
using ChronTravelers.Core.World;
using ChronTravelers.Engine.Combat;

namespace ChronTravelers.Engine.Npc;

/// <summary>
/// The per-tick behaviour loop for the monsters populating the year the
/// player is standing in (<see cref="YearPopulation"/>) — the monster
/// counterpart to <see cref="NpcController"/>. Each tick every living
/// monster heals if hurt (spending Ions, first converting a carried item
/// if broke), grabs loot off the floor of its room, or moves; monsters
/// sharing a room may fight each other, the loser dropping its carried
/// items plus a loot-table roll where it fell; a slow trickle respawns the
/// population back toward its soft cap.
///
/// A monster does <b>not</b> pursue or attack anyone who simply walks past.
/// Its behaviour is gated by an earned <see cref="Monster.Aggro"/> meter
/// (see <see cref="ChronTravelers.Core.Monsters.AggroModel"/>): stepping onto its
/// tile, lingering on/next to it, or shooting it raises aggro; moving away
/// bleeds it off. Below <c>AlertThreshold</c> the monster ignores the
/// player and wanders; at Alert it shadows (moves to close) but takes no
/// swing; only a <c>Hostile</c> monster lands an ambush hit — and only on
/// a turn the player spent idle, never in a <c>safeRoom</c> (a store), and
/// no more than once every <see cref="AmbushCooldownTicks"/> ticks.
/// Everything is thresholds/chances — original tuning, not GDD-specified.
/// </summary>
public static class MonsterController
{
    /// <summary>
    /// Per-tick chance a roaming monster takes a step. Deliberately low —
    /// a calm monster drifts slowly and randomly, so a player heading for
    /// one they saw on the <c>monsters</c> list actually finds it near
    /// where it was rather than chasing a same-speed target forever.
    /// </summary>
    private const double WanderChance = 0.28;

    private const double InfightChance = 0.20;
    private const double HealHpThreshold = 0.40;

    /// <summary>Idle top-up cadence when the year is within one of its soft cap — a slow trickle, rolled against <see cref="RespawnChance"/>.</summary>
    private const int RespawnCheckInterval = 12;

    /// <summary>Catch-up cadence when the year is <see cref="RespawnCatchUpDeficit"/>+ monsters short (e.g. just after an infight die-off) — fires every check, no roll, so a thinned year refills in a handful of turns instead of a minute.</summary>
    private const int RespawnCatchUpInterval = 4;

    /// <summary>Deficit (soft cap − living regular monsters) at or above which the fast catch-up cadence kicks in.</summary>
    private const int RespawnCatchUpDeficit = 2;

    private const double RespawnChance = 0.25;
    private const int DuelRoundCap = 200;

    /// <summary>After a step, the chance a roaming monster settles for a stretch (so it isn't drifting every eligible tick).</summary>
    private const double RestAfterWanderChance = 0.5;

    private const int RestTicksMin = 3;
    private const int RestTicksMax = 7;

    /// <summary>Minimum ticks between ambush hits on the player, so a quick <c>status</c> + <c>monsters</c> check near a hostile monster costs one hit, not three.</summary>
    private const int AmbushCooldownTicks = 2;

    /// <summary>
    /// How near (Manhattan) the player has to be for a monster to accrue
    /// aggro toward them and, once alerted, to move to close the distance.
    /// Kept at one room so "something stirs to the north" is a real
    /// approach.
    /// </summary>
    private const int AggroRange = 1;

    /// <param name="previousPlayerPosition">
    /// Where the player stood at the end of the last tick — lets a monster
    /// tell "stepped onto my tile" (a big aggro bump) from "was already
    /// standing here".
    /// </param>
    /// <param name="playerLingered">
    /// True only if the player spent this turn doing nothing (an
    /// informational command) and neither moved nor changed year. Only then
    /// can a hostile co-located monster ambush — acting (fighting, healing,
    /// shopping, moving) is always safe.
    /// </param>
    /// <param name="safeRooms">
    /// Rooms nothing will pursue, wander, or ambush into — the year's store
    /// tiles. A depot is a haven to shop and heal in.
    /// </param>
    /// <param name="narration">
    /// Optional sink for player-local ambient lines — a monster entering /
    /// leaving the player's room (with the direction), or first coming
    /// within earshot ("you hear something to the north").
    /// </param>
    /// <param name="year">
    /// The timeline year this population belongs to — used to tag the
    /// events it publishes so the console's inline feed can filter to the
    /// player's own year.
    /// </param>
    /// <param name="player">
    /// The player, when <paramref name="year"/> is the year they're
    /// standing in — drives aggro / shadowing / ambush / narration. Pass
    /// <c>null</c> to run a year the player isn't in (see
    /// <see cref="TickUnattended"/>): monsters still roam, infight, heal,
    /// grab loot and respawn, but nothing tracks or ambushes anyone.
    /// </param>
    public static void Tick(
        YearPopulation population,
        LevelMap map,
        IReadOnlyList<Func<Monster>> roster,
        int year,
        Traveler? player,
        Coordinate previousPlayerPosition,
        bool playerLingered,
        IRandomSource random,
        BroadcastChannel broadcast,
        IReadOnlySet<Coordinate>? safeRooms = null,
        ICollection<string>? narration = null)
    {
        var playerHere = player is not null && TimeScale.IsValidYear(player.CurrentYear);
        var playerSafe = playerHere && safeRooms is not null && safeRooms.Contains(player!.Position);

        foreach (var monster in population.Monsters.Where(m => !m.Health.IsDead).ToList())
        {
            var startedAt = monster.Position;
            var distance = playerHere ? ManhattanDistance(monster.Position, player!.Position) : int.MaxValue;

            // --- aggro accrual / decay -------------------------------------
            // An apex barely registers a passer-by (it picks its fights),
            // so every gain it would take is heavily scaled down.
            var aggroScale = monster.IsApex ? AggroModel.ApexAggroMultiplier : 1.0;

            if (!playerHere || playerSafe || distance > AggroRange)
            {
                monster.DecayAggro(AggroModel.DecayPerTick);
            }
            else if (player!.Position.Equals(monster.Position) && !previousPlayerPosition.Equals(monster.Position))
            {
                monster.RaiseAggro(AggroModel.EnterTileAggro * aggroScale); // stepped onto me
            }
            else if (distance == 0)
            {
                monster.RaiseAggro(AggroModel.CoLocatedPerTick * aggroScale); // parked on me
            }
            else
            {
                monster.RaiseAggro(AggroModel.AdjacentPerTick * aggroScale); // loitering next door
            }

            var mood = AggroModel.MoodFor(monster.Aggro);

            // --- movement ------------------------------------------------------
            if (!TryHeal(monster) && !TryGrabLoot(population, monster))
            {
                var shadowing = mood != AggroMood.Calm && playerHere && !playerSafe;

                if (shadowing && distance is > 0 and <= AggroRange)
                {
                    StepToward(map, monster, player!.Position, random, safeRooms);
                }
                else if (shadowing && distance == 0)
                {
                    // Locked on and toe to toe — hold.
                }
                else if (monster.RestTicks > 0)
                {
                    monster.RestTicks--; // settled in place for a stretch
                }
                else if (random.NextDouble() < (monster.IsApex ? WanderChance * 0.5 : WanderChance))
                {
                    // An apex lurks — it drifts half as often, so it stays a
                    // findable landmark you can walk up to and take on.
                    Wander(map, monster, random, safeRooms);
                    if (random.NextDouble() < RestAfterWanderChance)
                    {
                        monster.RestTicks = RestTicksMin + (int)(random.NextDouble() * (RestTicksMax - RestTicksMin + 1));
                    }
                }
            }

            if (narration is not null && playerHere && !monster.Position.Equals(startedAt))
            {
                NarrateMovement(narration, monster, startedAt, player!.Position, random);
            }

            monster.AdvanceIonRegenTick(IonEconomy.TicksPerIonRegen(monster.Tier, classDrainMultiplier: 1.0));
        }

        ResolveInfighting(population, random, broadcast, year);
        MaybeRespawn(population, map, roster, random);

        if (playerHere && playerLingered && !playerSafe && population.TicksSinceAmbush >= AmbushCooldownTicks
            && ResolveAmbush(population, player!, random, broadcast, year))
        {
            population.TicksSinceAmbush = 0;
        }

        population.TicksSinceAmbush++;
    }

    /// <summary>
    /// Runs one tick for a year the player is <b>not</b> standing in
    /// (docs/GDD.md §7.1): its monsters still drift, fight each other,
    /// heal, grab ground loot and respawn — so the timeline stays alive
    /// while you're elsewhere and a year you cleared doesn't stay a
    /// museum — but nothing tracks, shadows or ambushes anyone, and no
    /// player-local narration is produced. Infight kills post to the same
    /// broadcast channel, tagged with <paramref name="year"/>.
    /// </summary>
    public static void TickUnattended(
        YearPopulation population,
        LevelMap map,
        IReadOnlyList<Func<Monster>> roster,
        int year,
        IRandomSource random,
        BroadcastChannel broadcast,
        IReadOnlySet<Coordinate>? safeRooms = null)
        => Tick(population, map, roster, year, player: null,
            previousPlayerPosition: default, playerLingered: false,
            random, broadcast, safeRooms, narration: null);

    /// <summary>Hurt monster → heal (converting carried fodder first if out of Ions). True if it acted.</summary>
    private static bool TryHeal(Monster monster)
    {
        if (monster.Health.Current > monster.Health.Max * HealHpThreshold)
        {
            return false;
        }

        if (monster.Ions.Current == 0 && monster.Inventory.Count > 0)
        {
            // Burn scavenged fodder, not the weapon it's wielding — unless
            // that's all it's carrying.
            var fuel = monster.Inventory.FirstOrDefault(i => !ReferenceEquals(i, monster.EquippedWeapon))
                ?? monster.Inventory[0];
            monster.Convert(fuel);
        }

        return monster.Heal() > 0;
    }

    /// <summary>Fraction of its Ion pool below which a monster will scavenge a ground item to burn for fuel.</summary>
    private const double ScavengeForIonsBelow = 0.5;

    /// <summary>
    /// A monster won't pick up a floor weapon whose <see cref="Item.AttackBonus"/>
    /// exceeds this multiple of its own base <see cref="Monster.AttackPower"/>.
    /// It can arm up — a scavenged weapon still meaningfully raises its
    /// hits — but it can't turn a common tier-1 roamer into something that
    /// one-shots a fresh Traveler by grabbing a Rare/Legendary blade off
    /// the ground (playtest feedback). Scales with tier for free, since the
    /// base attack does.
    /// </summary>
    private const double ScavengeWeaponAttackCap = 1.4;

    /// <summary>
    /// A monster only takes loot off the floor for a reason (docs/GDD.md
    /// §7.1): one item to burn for Ions when it's running low, or a single
    /// weapon that beats what it's wielding. Otherwise it steps over the
    /// pile. Returns true if it took something.
    /// </summary>
    private static bool TryGrabLoot(YearPopulation population, Monster monster)
    {
        if (population.LootAt(monster.Position).Count == 0)
        {
            return false;
        }

        // (a) Low on Ions → grab one thing to convert. Prefer junk/consumables
        // so a good weapon on the ground survives for the player. Never a
        // Time Shard — those are the player's alone.
        if (monster.Ions.Current < monster.Ions.Max * ScavengeForIonsBelow)
        {
            var fuel = population.TakeGroundLoot(monster.Position, i => !i.IsTimeShard && i.Type is not (ItemType.Weapon or ItemType.Armor))
                       ?? population.TakeGroundLoot(monster.Position, i => !i.IsTimeShard);
            if (fuel is not null)
            {
                monster.AddToInventory(fuel);
                return true;
            }
        }

        // (b) A ground weapon better than what it's wielding → upgrade,
        // dropping the old one back for someone else. Capped at
        // ScavengeWeaponAttackCap× its base attack so it can't grab
        // something far above its weight class.
        var currentBonus = monster.EquippedWeapon?.AttackBonus ?? 0;
        var scavengeCeiling = (int)Math.Round(monster.AttackPower * ScavengeWeaponAttackCap);
        var upgrade = population.TakeGroundLoot(monster.Position,
            i => !i.IsTimeShard && i.Type == ItemType.Weapon
                 && i.AttackBonus > currentBonus && i.AttackBonus <= scavengeCeiling);
        if (upgrade is not null)
        {
            if (monster.EquippedWeapon is { } old)
            {
                monster.RemoveFromInventory(old);
                population.AddGroundLoot(monster.Position, old);
            }

            monster.EquipWeapon(upgrade);
            return true;
        }

        return false;
    }

    private static bool IsBlocked(IReadOnlySet<Coordinate>? safeRooms, Coordinate room) =>
        safeRooms is not null && safeRooms.Contains(room);

    /// <summary>
    /// Drift movement: a roaming monster picks a random open exit (never
    /// into a haven) and takes it. No fixed patrol heading — combined with
    /// the low <see cref="WanderChance"/> it moves slowly and
    /// unpredictably. <see cref="Monster.Heading"/> is left pointing the
    /// way it last stepped, purely so movement narration reads right.
    /// </summary>
    private static void Wander(LevelMap map, Monster monster, IRandomSource random, IReadOnlySet<Coordinate>? safeRooms)
    {
        var exits = map.GetRoom(monster.Position).ExitDescriptions.Keys.ToList();
        if (exits.Count == 0)
        {
            return;
        }

        bool Usable(Direction dir)
        {
            if (!exits.Contains(dir))
            {
                return false;
            }

            var step = map.TryMove(monster.Position, dir);
            return step.Success && !IsBlocked(safeRooms, step.Destination!.Value);
        }

        var options = exits.Where(Usable).ToList();
        if (options.Count == 0)
        {
            return;
        }

        var heading = options[(int)(random.NextDouble() * options.Count)];
        monster.Heading = heading;
        var move = map.TryMove(monster.Position, heading);
        if (move.Success)
        {
            monster.MoveTo(move.Destination!.Value);
        }
    }

    /// <summary>Steps one room along whichever exit most reduces the Manhattan distance to <paramref name="target"/> (never into a <paramref name="safeRooms"/> tile); holds if none does.</summary>
    private static void StepToward(LevelMap map, Monster monster, Coordinate target, IRandomSource random, IReadOnlySet<Coordinate>? safeRooms)
    {
        var exits = map.GetRoom(monster.Position).ExitDescriptions.Keys
            .OrderBy(_ => random.NextDouble()) // break ties without always favouring one axis
            .ToList();

        var bestDistance = ManhattanDistance(monster.Position, target);
        Coordinate? bestRoom = null;

        foreach (var dir in exits)
        {
            var move = map.TryMove(monster.Position, dir);
            if (!move.Success || IsBlocked(safeRooms, move.Destination!.Value))
            {
                continue;
            }

            var distance = ManhattanDistance(move.Destination.Value, target);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestRoom = move.Destination.Value;
            }
        }

        if (bestRoom is { } room)
        {
            monster.MoveTo(room);
        }
    }

    private static int ManhattanDistance(Coordinate a, Coordinate b) =>
        Math.Abs(a.East - b.East) + Math.Abs(a.North - b.North);

    /// <summary>The cardinal direction you'd step from <paramref name="from"/> toward <paramref name="to"/> (dominant axis).</summary>
    private static Direction DirectionBetween(Coordinate from, Coordinate to)
    {
        var de = to.East - from.East;
        var dn = to.North - from.North;
        if (Math.Abs(de) >= Math.Abs(dn))
        {
            return de >= 0 ? Direction.East : Direction.West;
        }

        return dn >= 0 ? Direction.North : Direction.South;
    }

    private static readonly string[] EarshotLines =
    [
        "You hear something moving to the {0}.",
        "Something stirs in the room to the {0}.",
        "You catch movement off to the {0}.",
        "A shape shifts about to the {0}.",
    ];

    private static readonly string[] EntersLines =
    [
        "{0} comes in from the {1}.",
        "{0} shoulders in from the {1}.",
        "{0} pads in from the {1}.",
    ];

    private static readonly string[] LeavesLines =
    [
        "The {0} moves off to the {1}.",
        "The {0} slips away to the {1}.",
        "The {0} breaks {1}.",
    ];

    private static string WithArticle(string name)
    {
        var vowel = name.Length > 0 && "AEIOUaeiou".Contains(name[0]);
        return (vowel ? "An " : "A ") + name;
    }

    /// <summary>Adds a player-local line when a monster's move crosses into/out of the player's room, or first comes within one room.</summary>
    private static void NarrateMovement(ICollection<string> narration, Monster monster, Coordinate from, Coordinate playerPos, IRandomSource random)
    {
        var to = monster.Position;
        var wasHere = from.Equals(playerPos);
        var nowHere = to.Equals(playerPos);

        string Pick(string[] lines) => lines[Math.Min(lines.Length - 1, (int)(random.NextDouble() * lines.Length))];

        if (nowHere && !wasHere)
        {
            narration.Add(string.Format(Pick(EntersLines), WithArticle(monster.Name), DirectionBetween(playerPos, from).Name()));
            return;
        }

        if (wasHere && !nowHere)
        {
            narration.Add(string.Format(Pick(LeavesLines), monster.Name, DirectionBetween(playerPos, to).Name()));
            return;
        }

        // First time within one room of the player (crossed from farther out).
        if (ManhattanDistance(to, playerPos) == 1 && ManhattanDistance(from, playerPos) > 1)
        {
            narration.Add(string.Format(Pick(EarshotLines), DirectionBetween(playerPos, to).Name()));
        }
    }

    private static void ResolveInfighting(YearPopulation population, IRandomSource random, BroadcastChannel broadcast, int year)
    {
        var crowdedRooms = population.Monsters
            .Where(m => !m.Health.IsDead)
            .GroupBy(m => m.Position)
            .Where(g => g.Count() >= 2)
            .ToList();

        foreach (var room in crowdedRooms)
        {
            if (random.NextDouble() >= InfightChance)
            {
                continue;
            }

            var pair = room.OrderBy(m => m.Health.Current).Take(2).ToList();
            var winner = Duel(pair[0], pair[1], random);
            var loser = ReferenceEquals(winner, pair[0]) ? pair[1] : pair[0];

            foreach (var item in loser.Inventory.ToList())
            {
                population.AddGroundLoot(loser.Position, item);
            }

            foreach (var item in LootDropRoller.RollForKill(loser, random))
            {
                population.AddGroundLoot(loser.Position, item);
            }

            population.RemoveMonster(loser);
            broadcast.Publish(GameEvent.Slain(loser.Name, winner.Name, year, victimIsCreature: true, killerIsCreature: true));
        }
    }

    /// <summary>A compact auto-resolved fight between two monsters — reuses <see cref="CombatResolver.RollDamage"/>. Returns the survivor (the faster monster wins a mutual-kill tie, which can't actually happen since damage is always ≥ 1).</summary>
    private static Monster Duel(Monster x, Monster y, IRandomSource random)
    {
        var (first, second) = x.Speed >= y.Speed ? (x, y) : (y, x);
        var guard = 0;

        while (!first.Health.IsDead && !second.Health.IsDead && guard++ < DuelRoundCap)
        {
            second.Health.Damage(CombatResolver.RollDamage(first.EffectiveAttackPower, second.Defense, random));
            if (second.Health.IsDead)
            {
                break;
            }

            first.Health.Damage(CombatResolver.RollDamage(second.EffectiveAttackPower, first.Defense, random));
        }

        return first.Health.IsDead ? second : first;
    }

    /// <summary>
    /// A <see cref="AggroMood.Hostile"/> monster sharing the player's room
    /// lands one free hit. Only the hardest-hitting such monster attacks
    /// per tick, so a crowded room is dangerous but not instantly lethal.
    /// The player avoids it by <c>fight</c>ing (resolved before the tick),
    /// leaving, or just never having provoked it.
    /// </summary>
    /// <returns>True if a monster actually landed a hit (drives the cooldown reset).</returns>
    private static bool ResolveAmbush(YearPopulation population, Traveler player, IRandomSource random, BroadcastChannel broadcast, int year)
    {
        if (player.Health.IsDead)
        {
            return false;
        }

        var attacker = population.Monsters
            .Where(m => !m.Health.IsDead
                && m.Position.Equals(player.Position)
                && AggroModel.MoodFor(m.Aggro) == AggroMood.Hostile)
            .OrderByDescending(m => m.EffectiveAttackPower)
            .FirstOrDefault();

        if (attacker is null)
        {
            return false;
        }

        // An ambush catches you unbraced — only half your defense applies, so
        // a lingering low-tier monster still stings rather than pinging for 1.
        var dealt = player.Health.Damage(CombatResolver.RollDamage(attacker.EffectiveAttackPower, player.EffectiveDefense / 2, random));
        broadcast.Publish(GameEvent.Ambushed(attacker.Name, player.Name, dealt, year));
        return true;
    }

    private static void MaybeRespawn(
        YearPopulation population,
        LevelMap map,
        IReadOnlyList<Func<Monster>> roster,
        IRandomSource random)
    {
        population.TicksSinceRespawn++;

        // SoftCap counts the regular roster only — a seeded apex shouldn't
        // starve the ordinary respawn trickle.
        var livingRegular = population.Monsters.Count(m => !m.Health.IsDead && !m.IsApex);
        var deficit = population.SoftCap - livingRegular;
        if (roster.Count == 0 || deficit <= 0)
        {
            // At or over cap — nothing to do; don't let the timer run away so
            // the next real die-off starts its countdown from zero.
            population.TicksSinceRespawn = 0;
            return;
        }

        // A big deficit (a die-off) refills fast and unconditionally; the
        // last one or two trickle back on the slow rolled cadence.
        var catchingUp = deficit >= RespawnCatchUpDeficit;
        var interval = catchingUp ? RespawnCatchUpInterval : RespawnCheckInterval;
        if (population.TicksSinceRespawn < interval)
        {
            return;
        }

        population.TicksSinceRespawn = 0;

        if (!catchingUp && random.NextDouble() >= RespawnChance)
        {
            return;
        }

        var freeRooms = map.Rooms.Keys.Where(c => !population.HasLivingMonsterAt(c)).ToList();
        if (freeRooms.Count == 0)
        {
            return;
        }

        var monster = roster[(int)(random.NextDouble() * roster.Count)]();
        monster.PlaceAt(freeRooms[(int)(random.NextDouble() * freeRooms.Count)]);
        population.AddMonster(monster);
    }
}
