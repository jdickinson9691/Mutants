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
/// if broke), grabs loot off the floor of its room, or wanders through an
/// exit; monsters sharing a room may fight each other, the loser dropping
/// its carried items plus a loot-table roll where it fell; and a slow
/// trickle respawns the population back toward its soft cap. Everything
/// is thresholds/chances — original tuning, not GDD-specified.
/// </summary>
public static class MonsterController
{
    private const double WanderChance = 0.35;
    private const double InfightChance = 0.20;
    private const double HealHpThreshold = 0.40;
    private const int RespawnCheckInterval = 12;
    private const double RespawnChance = 0.25;
    private const int DuelRoundCap = 200;

    public static void Tick(
        YearPopulation population,
        LevelMap map,
        IReadOnlyList<Func<Monster>> roster,
        IRandomSource random,
        BroadcastChannel broadcast)
    {
        foreach (var monster in population.Monsters.Where(m => !m.Health.IsDead).ToList())
        {
            if (!TryHeal(monster) && !TryGrabLoot(population, monster) && random.NextDouble() < WanderChance)
            {
                Wander(map, monster, random);
            }

            monster.AdvanceIonRegenTick(IonEconomy.TicksPerIonRegen(monster.Tier, classDrainMultiplier: 1.0));
        }

        ResolveInfighting(population, random, broadcast);
        MaybeRespawn(population, map, roster, random);
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
