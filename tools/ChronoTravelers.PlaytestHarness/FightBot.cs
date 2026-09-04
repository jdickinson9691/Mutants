using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Monsters;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Engine;
using ChronoTravelers.Engine.Combat;
using ChronoTravelers.Engine.Content;

namespace ChronoTravelers.PlaytestHarness;

/// <summary>
/// Drives one spatial <see cref="CombatSession"/> fight to its end, with a
/// "greedy on-cooldown" ability policy: every round, cast the
/// highest-scoring unlocked/affordable ability (see <see cref="ScoreAbility"/>
/// — the same priority order <c>NpcController.ChooseAbility</c> uses for NPC
/// grind fights, minus its per-round "even bother casting" chance roll,
/// since this harness wants maximum ability-usage data per run, not a
/// realistic-looking cast rate), else a plain attack. Loot from a win falls
/// to the ground at the bot's position, same as a real fight.
/// </summary>
public static class FightBot
{
    public static void Fight(Traveler bot, Monster monster, IReadOnlyList<AbilityData> classAbilities, IRandomSource random, RunReport report, YearPopulation population)
    {
        var castable = classAbilities
            .Where(a => a.Level <= bot.Level && !string.Equals(a.Effect, "None", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var session = new CombatSession(bot, monster, random);

        while (!session.IsOver)
        {
            var chosen = castable.Count > 0 ? ChooseAbility(bot, monster, castable) : null;
            if (chosen is not null)
            {
                var usage = report.AbilityUsage.TryGetValue(chosen.Name, out var u) ? u : report.AbilityUsage[chosen.Name] = new AbilityUsage();
                usage.Attempts++;

                var result = session.Cast(chosen);
                if (result.Success)
                {
                    usage.Successes++;
                    continue;
                }

                usage.Failures++;
            }

            session.Attack();
        }

        if (session.TravelerWon)
        {
            foreach (var item in session.ItemsDropped.Concat(monster.Inventory))
            {
                population.AddGroundLoot(bot.Position, item);
            }

            report.Kills++;
            report.TotalXp += session.XpAwarded;
        }
    }

    private static AbilityData? ChooseAbility(Traveler bot, Monster monster, List<AbilityData> castable)
    {
        var hpFraction = bot.Health.Max > 0 ? bot.Health.Current / (double)bot.Health.Max : 1.0;

        AbilityData? best = null;
        var bestScore = 0.0;

        foreach (var ability in castable)
        {
            if (!bot.Tachyons.CanAfford(bot.EffectiveCastCost(ability.TachyonCost)))
            {
                continue;
            }

            var score = ScoreAbility(ability, bot, monster, hpFraction);
            if (score > bestScore)
            {
                bestScore = score;
                best = ability;
            }
        }

        return best;
    }

    /// <summary>Same priority order as <c>NpcController.ScoreAbility</c> — an outright win button always wins, Heal scales with how hurt the bot is, a conditioned Damage ability gets a bonus when its condition is currently met, Restore Tachyons only matters when the pool is actually low.</summary>
    private static double ScoreAbility(AbilityData ability, Traveler bot, Monster monster, double hpFraction)
    {
        if (!Enum.TryParse<AbilityEffectType>(ability.Effect, ignoreCase: true, out var effect))
        {
            return -1;
        }

        var conditionBonus = !string.IsNullOrEmpty(ability.Condition) && ConditionCurrentlyMet(ability.Condition, ability.Tag, monster) ? 5.0 : 0.0;

        return effect switch
        {
            AbilityEffectType.InstantDefeatNonBoss => 1000,
            AbilityEffectType.Heal => (1.0 - hpFraction) * 25,
            AbilityEffectType.DamageOverTime => 12 + ability.Magnitude + conditionBonus,
            AbilityEffectType.ExtraAttack => 11,
            AbilityEffectType.IgnoreDefenseDamage => 10 + ability.Magnitude + conditionBonus,
            AbilityEffectType.Damage => 9 + ability.Magnitude + conditionBonus,
            AbilityEffectType.GuaranteedCritNextAttack => 8,
            AbilityEffectType.DebuffTargetDefense => 7,
            AbilityEffectType.DebuffTargetAttack => 6,
            AbilityEffectType.DebuffTargetSpeed => 5,
            AbilityEffectType.BuffSelfAttack => 5,
            AbilityEffectType.BuffSelfDefense => 4,
            AbilityEffectType.Shield => 4,
            AbilityEffectType.RestoreTachyons => bot.Tachyons.Current < bot.Tachyons.Max * 0.3 ? 15 : 0,
            _ => -1,
        };
    }

    private static bool ConditionCurrentlyMet(string condition, string? tag, Monster monster) => condition switch
    {
        "TargetUndamaged" => monster.Health.Current == monster.Health.Max,
        "TargetBelow25Percent" => monster.Health.Current <= monster.Health.Max * 0.25,
        "TargetTagged" => tag is not null && monster.HasTag(tag),
        _ => true,
    };
}
