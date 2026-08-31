using Mutants.Core.Characters;
using Mutants.Core.Events;
using Mutants.Core.Ions;
using Mutants.Core.Monsters;
using Mutants.Core.Time;
using Mutants.Core.World;
using Mutants.Engine.Combat;

namespace Mutants.Engine.Npc;

/// <summary>
/// The per-tick behaviour loop for the monsters populating the year the
/// player is standing in (<see cref="YearPopulation"/>) — the monster
/// counterpart to <see cref="NpcController"/>. Each tick every living
/// monster heals if hurt (spending Ions, first converting a carried item
/// if broke), grabs loot off the floor of its room, closes on the player
/// if it's within <see cref="AggroRange"/> (otherwise wanders through a
/// random exit); monsters sharing a room may fight each other, the loser
/// dropping its carried items plus a loot-table roll where it fell; a slow
/// trickle respawns the population back toward its soft cap; and finally a
/// monster standing in the player's room lands one ambush hit — so
/// lingering next to a monster instead of fighting or fleeing costs HP.
/// Everything is thresholds/chances — original tuning, not GDD-specified.
/// </summary>
public static class MonsterController
{
    private const double WanderChance = 0.25;
    private const double InfightChance = 0.20;
    private const double HealHpThreshold = 0.40;
    private const int RespawnCheckInterval = 12;
    private const double RespawnChance = 0.25;
    private const int DuelRoundCap = 200;

    /// <summary>Minimum ticks between ambush hits on the player, so a quick <c>status</c> + <c>monsters</c> check near a monster costs one hit, not three.</summary>
    private const int AmbushCooldownTicks = 2;

    /// <summary>
    /// A monster this many rooms (Manhattan) from the player or nearer
    /// stops wandering and moves to close the distance; one that's already
    /// in the player's room holds position rather than drifting off. Kept
    /// deliberately short so "something stirs to the north" (one room away)
    /// is a real approach, not a monster that vanishes before you can act.
    /// </summary>
    private const int AggroRange = 1;

    /// <param name="playerLingered">
    /// True if the player neither moved nor changed year since the last
    /// tick. Only a lingering player gets ambushed — arriving in a room (or
    /// travelling into a year) always buys one free turn to size it up.
    /// </param>
    public static void Tick(
        YearPopulation population,
        LevelMap map,
        IReadOnlyList<Func<Monster>> roster,
        Mutant player,
        bool playerLingered,
        IRandomSource random,
        BroadcastChannel broadcast)
    {
        var playerHere = TimeScale.IsValidYear(player.CurrentYear);

        foreach (var monster in population.Monsters.Where(m => !m.Health.IsDead).ToList())
        {
            if (!TryHeal(monster) && !TryGrabLoot(population, monster))
            {
                var distance = playerHere ? ManhattanDistance(monster.Position, player.Position) : int.MaxValue;

                if (distance == 0)
                {
                    // Toe to toe with the player — hold, don't wander off.
                }
                else if (distance <= AggroRange)
                {
                    StepToward(map, monster, player.Position, random);
                }
                else if (random.NextDouble() < WanderChance)
                {
                    Wander(map, monster, random);
                }
            }

            monster.AdvanceIonRegenTick(IonEconomy.TicksPerIonRegen(monster.Tier, classDrainMultiplier: 1.0));
        }

        ResolveInfighting(population, random, broadcast);
        MaybeRespawn(population, map, roster, random);

        if (playerHere && playerLingered && population.TicksSinceAmbush >= AmbushCooldownTicks
            && ResolveAmbush(population, player, random, broadcast))
        {
            population.TicksSinceAmbush = 0;
        }

        population.TicksSinceAmbush++;
    }

    /// <summary>Hurt monster → heal (converting a carried item first if out of Ions). True if it acted.</summary>
    private static bool TryHeal(Monster monster)
    {
        if (monster.Health.Current > monster.Health.Max * HealHpThreshold)
        {
            return false;
        }

        if (monster.Ions.Current == 0 && monster.Inventory.Count > 0)
        {
            monster.Convert(monster.Inventory[0]);
        }

        return monster.Heal() > 0;
    }

    private static bool TryGrabLoot(YearPopulation population, Monster monster)
    {
        var item = population.TakeGroundLoot(monster.Position, _ => true);
        if (item is null)
        {
            return false;
        }

        monster.AddToInventory(item);
        return true;
    }

    private static void Wander(LevelMap map, Monster monster, IRandomSource random)
    {
        var exits = map.GetRoom(monster.Position).ExitDescriptions.Keys.ToList();
        if (exits.Count == 0)
        {
            return;
        }

        var move = map.TryMove(monster.Position, exits[(int)(random.NextDouble() * exits.Count)]);
        if (move.Success)
        {
            monster.MoveTo(move.Destination!.Value);
        }
    }

    /// <summary>Steps one room along whichever exit most reduces the Manhattan distance to <paramref name="target"/>; holds if none does.</summary>
    private static void StepToward(LevelMap map, Monster monster, Coordinate target, IRandomSource random)
    {
        var exits = map.GetRoom(monster.Position).ExitDescriptions.Keys
            .OrderBy(_ => random.NextDouble()) // break ties without always favouring one axis
            .ToList();

        var bestDistance = ManhattanDistance(monster.Position, target);
        Coordinate? bestRoom = null;

        foreach (var dir in exits)
        {
            var move = map.TryMove(monster.Position, dir);
            if (!move.Success)
            {
                continue;
            }

            var distance = ManhattanDistance(move.Destination!.Value, target);
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

    private static void ResolveInfighting(YearPopulation population, IRandomSource random, BroadcastChannel broadcast)
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

            foreach (var item in LootDropRoller.Roll(loser.LootTable, random))
            {
                population.AddGroundLoot(loser.Position, item);
            }

            population.RemoveMonster(loser);
            broadcast.Publish(GameEvent.Slain(loser.Name, winner.Name));
        }
    }

    /// <summary>A compact auto-resolved fight between two monsters — reuses <see cref="CombatResolver.RollDamage"/>. Returns the survivor (the faster monster wins a mutual-kill tie, which can't actually happen since damage is always ≥ 1).</summary>
    private static Monster Duel(Monster x, Monster y, IRandomSource random)
    {
        var (first, second) = x.Speed >= y.Speed ? (x, y) : (y, x);
        var guard = 0;

        while (!first.Health.IsDead && !second.Health.IsDead && guard++ < DuelRoundCap)
        {
            second.Health.Damage(CombatResolver.RollDamage(first.AttackPower, second.Defense, random));
            if (second.Health.IsDead)
            {
                break;
            }

            first.Health.Damage(CombatResolver.RollDamage(second.AttackPower, first.Defense, random));
        }

        return first.Health.IsDead ? second : first;
    }

    /// <summary>
    /// A living monster sharing the player's room lands one free hit. Only
    /// the hardest-hitting co-located monster attacks per tick, so a
    /// crowded room is dangerous but not instantly lethal. The player
    /// avoids it by <c>fight</c>ing (resolved before the tick) or leaving.
    /// </summary>
    /// <returns>True if a monster actually landed a hit (drives the cooldown reset).</returns>
    private static bool ResolveAmbush(YearPopulation population, Mutant player, IRandomSource random, BroadcastChannel broadcast)
    {
        if (player.Health.IsDead)
        {
            return false;
        }

        var attacker = population.Monsters
            .Where(m => !m.Health.IsDead && m.Position.Equals(player.Position))
            .OrderByDescending(m => m.AttackPower)
            .FirstOrDefault();

        if (attacker is null)
        {
            return false;
        }

        // An ambush catches you unbraced — only half your defense applies, so
        // a lingering low-tier monster still stings rather than pinging for 1.
        var dealt = player.Health.Damage(CombatResolver.RollDamage(attacker.AttackPower, player.EffectiveDefense / 2, random));
        broadcast.Publish(GameEvent.Ambushed(attacker.Name, player.Name, dealt));
        return true;
    }

    private static void MaybeRespawn(
        YearPopulation population,
        LevelMap map,
        IReadOnlyList<Func<Monster>> roster,
        IRandomSource random)
    {
        population.TicksSinceRespawn++;
        if (population.TicksSinceRespawn < RespawnCheckInterval)
        {
            return;
        }

        population.TicksSinceRespawn = 0;

        if (roster.Count == 0
            || population.Monsters.Count(m => !m.Health.IsDead) >= population.SoftCap
            || random.NextDouble() >= RespawnChance)
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
