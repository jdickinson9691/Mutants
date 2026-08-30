using Mutants.Core.Characters;
using Mutants.Core.Monsters;

namespace Mutants.Engine.Combat;

/// <summary>
/// Resolves a fight between a Mutant and a Monster — docs/AGENTS.md
/// assigns "combat resolution" to the Systems/Engine Agent, kept separate
/// from the Core domain model. docs/GDD.md confirms "every class shares
/// ... a primary attack" but specifies no combat formula; the turn order,
/// damage formula, and variance below are original design pending Design
/// Agent sign-off (same convention as the class HP/Ion tuning in
/// Mutants.Core.Classes.ClassDefinition).
/// </summary>
public static class CombatResolver
{
    /// <summary>
    /// Safety valve against an unbounded loop. Damage is always at least 1
    /// against a finite HP pool, so a real fight always ends well before
    /// this — it only guards against a future change breaking that
    /// invariant.
    /// </summary>
    private const int MaxRounds = 200;

    /// <summary>
    /// Damage variance band applied on top of (attack - defense): a raw
    /// hit is scaled by a random factor in [1 - Variance, 1 + Variance].
    /// </summary>
    private const double DamageVariance = 0.15;

    /// <summary>
    /// Fights <paramref name="mutant"/> against <paramref name="monster"/>
    /// to a decisive result (or the round cap). On a Mutant win, awards XP
    /// and rolls/adds loot per docs/GDD.md §5. On a loss (or the round
    /// cap, treated as a loss), no rewards are granted and combat/death
    /// handling beyond that — docs/GDD.md §3.3's death & recall — is out
    /// of scope here; the caller decides what a defeat means.
    /// </summary>
    public static FightResult Fight(Mutant mutant, Monster monster, IRandomSource random)
    {
        var log = new List<string>();
        var mutantActsFirst = mutant.Speed >= monster.Speed;
        var rounds = 0;

        while (!mutant.Health.IsDead && !monster.Health.IsDead && rounds < MaxRounds)
        {
            rounds++;

            if (mutantActsFirst)
            {
                ResolveAttack(mutant.Name, mutant.EffectiveAttackPower, monster.Name, monster.Defense, monster.Health, random, log);
                if (monster.Health.IsDead)
                {
                    break;
                }

                ResolveAttack(monster.Name, monster.AttackPower, mutant.Name, mutant.EffectiveDefense, mutant.Health, random, log);
            }
            else
            {
                ResolveAttack(monster.Name, monster.AttackPower, mutant.Name, mutant.EffectiveDefense, mutant.Health, random, log);
                if (mutant.Health.IsDead)
                {
                    break;
                }

                ResolveAttack(mutant.Name, mutant.EffectiveAttackPower, monster.Name, monster.Defense, monster.Health, random, log);
            }
        }

        var mutantWon = monster.Health.IsDead && !mutant.Health.IsDead;

        if (!mutantWon)
        {
            return new FightResult(MutantWon: false, rounds, XpAwarded: 0, ItemsDropped: [], log);
        }

        var levelsGained = mutant.GainXp(monster.XpReward);
        if (levelsGained > 0)
        {
            log.Add($"{mutant.Name} gained {levelsGained} level(s)!");
        }

        var loot = LootDropRoller.Roll(monster.LootTable, random);
        foreach (var item in loot)
        {
            mutant.AddToInventory(item);
            log.Add($"{mutant.Name} looted {item.Name}.");
        }

        return new FightResult(MutantWon: true, rounds, XpAwarded: monster.XpReward, ItemsDropped: loot, log);
    }

    private static void ResolveAttack(
        string attackerName, int attackerPower,
        string defenderName, int defenderDefense,
        Core.Stats.HealthPool defenderHealth,
        IRandomSource random, List<string> log)
    {
        var raw = attackerPower - defenderDefense;
        var varianceFactor = 1 - DamageVariance + random.NextDouble() * (2 * DamageVariance);
        var damage = Math.Max(1, (int)Math.Round(raw * varianceFactor));

        var actualDamage = defenderHealth.Damage(damage);
        log.Add($"{attackerName} hits {defenderName} for {actualDamage} damage.");
    }
}
