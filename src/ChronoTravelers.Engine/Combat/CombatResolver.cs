using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Monsters;

namespace ChronoTravelers.Engine.Combat;

/// <summary>
/// Resolves a fight between a Traveler and a Monster — docs/AGENTS.md
/// assigns "combat resolution" to the Systems/Engine Agent, kept separate
/// from the Core domain model. docs/GDD.md confirms "every class shares
/// ... a primary attack" but specifies no combat formula; the turn order,
/// damage formula, and variance below are original design pending Design
/// Agent sign-off (same convention as the class HP/Tachyon tuning in
/// ChronoTravelers.Core.Classes.ClassDefinition).
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
    /// Armour-penetration floor: however much defence exceeds attack, a
    /// hit still lands at least this fraction of the attacker's power
    /// (before variance). Stops heavy armour from reducing every hit to 1
    /// — mid/late fights against a well-armoured character now cost real
    /// HP. Only bites when defence &gt; 0.70 × attack, i.e. essentially
    /// only against an armoured player; monster-vs-monster and
    /// player-vs-monster (low monster defence) are unaffected.
    /// </summary>
    private const double MinDamageFraction = 0.30;

    /// <summary>
    /// Fights <paramref name="traveler"/> against <paramref name="monster"/>
    /// to a decisive result (or the round cap). On a Traveler win, awards XP
    /// and rolls/adds loot per docs/GDD.md §5. On a loss (or the round
    /// cap, treated as a loss), no rewards are granted and combat/death
    /// handling beyond that — docs/GDD.md §3.3's death & recall — is out
    /// of scope here; the caller decides what a defeat means.
    /// </summary>
    public static FightResult Fight(Traveler traveler, Monster monster, IRandomSource random)
    {
        var log = new List<string>();
        var travelerActsFirst = traveler.Speed >= monster.Speed;
        var rounds = 0;

        while (!traveler.Health.IsDead && !monster.Health.IsDead && rounds < MaxRounds)
        {
            rounds++;

            if (travelerActsFirst)
            {
                ResolveAttack(traveler.Name, traveler.EffectiveAttackPower, monster.Name, monster.Defense, monster.Health, random, log);
                if (monster.Health.IsDead)
                {
                    break;
                }

                ResolveAttack(monster.Name, monster.EffectiveAttackPower, traveler.Name, traveler.EffectiveDefense, traveler.Health, random, log);
            }
            else
            {
                ResolveAttack(monster.Name, monster.EffectiveAttackPower, traveler.Name, traveler.EffectiveDefense, traveler.Health, random, log);
                if (traveler.Health.IsDead)
                {
                    break;
                }

                ResolveAttack(traveler.Name, traveler.EffectiveAttackPower, monster.Name, monster.Defense, monster.Health, random, log);
            }
        }

        var travelerWon = monster.Health.IsDead && !traveler.Health.IsDead;

        if (!travelerWon)
        {
            return new FightResult(TravelerWon: false, rounds, XpAwarded: 0, ItemsDropped: [], log);
        }

        var loot = AwardVictory(traveler, monster, random, log, out var xpAwarded);
        return new FightResult(TravelerWon: true, rounds, XpAwarded: xpAwarded, ItemsDropped: loot, log);
    }

    /// <summary>
    /// Grants XP and rolls loot for defeating <paramref name="monster"/> —
    /// shared with CombatSession's interactive fights. XP is the
    /// outlevel-scaled amount (<see cref="MonsterScaling.KillXp"/>), surfaced
    /// via <paramref name="xpAwarded"/> so callers report what was actually
    /// granted. When <paramref name="addToInventory"/> is true (the abstract
    /// NPC path) the loot goes straight into the winner's pack; when false
    /// (the player's interactive fight) it's just returned, and the caller
    /// drops it on the ground for the player to <c>take</c>.
    /// </summary>
    internal static IReadOnlyList<Core.Items.Item> AwardVictory(
        Traveler traveler, Monster monster, IRandomSource random, List<string> log, out int xpAwarded, bool addToInventory = true)
    {
        xpAwarded = MonsterScaling.KillXp(monster.XpReward, monster.Tier, traveler.Level);
        var levelsGained = traveler.GainXp(xpAwarded);
        if (levelsGained > 0)
        {
            log.Add($"{traveler.Name} gained {levelsGained} level(s)!");
        }

        var loot = LootDropRoller.RollForKill(monster, random);
        if (addToInventory)
        {
            foreach (var item in loot)
            {
                traveler.AddToInventory(item);
                log.Add($"{traveler.Name} looted {item.Name}.");
            }
        }

        return loot;
    }

    /// <summary>Rolls one attack's damage with the standard variance band — shared with CombatSession.</summary>
    internal static int RollDamage(int attackerPower, int defenderDefense, IRandomSource random)
    {
        var raw = Math.Max(attackerPower - defenderDefense, attackerPower * MinDamageFraction);
        var varianceFactor = 1 - DamageVariance + random.NextDouble() * (2 * DamageVariance);
        return Math.Max(1, (int)Math.Round(raw * varianceFactor));
    }

    private static void ResolveAttack(
        string attackerName, int attackerPower,
        string defenderName, int defenderDefense,
        Core.Stats.HealthPool defenderHealth,
        IRandomSource random, List<string> log)
    {
        var damage = RollDamage(attackerPower, defenderDefense, random);
        var actualDamage = defenderHealth.Damage(damage);
        log.Add($"{attackerName} hits {defenderName} for {actualDamage} damage.");
    }
}
