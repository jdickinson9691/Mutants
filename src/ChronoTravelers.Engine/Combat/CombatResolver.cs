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
    /// Damage variance band applied on top of the mitigated raw hit: it's
    /// scaled by a random factor in [1 - Variance, 1 + Variance].
    /// </summary>
    private const double DamageVariance = 0.15;

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

        // Passives that only mean something "this fight" (Juggernaut
        // Momentum's streak, Unbreakable's once-per-fight save) start fresh
        // every time — see docs/GDD.md §4.2.1.
        traveler.ResetPerFightState();

        while (!traveler.Health.IsDead && !monster.Health.IsDead && rounds < MaxRounds)
        {
            rounds++;

            if (travelerActsFirst)
            {
                AttackMonster(traveler, monster, random, log);
                if (monster.Health.IsDead)
                {
                    break;
                }

                AttackTraveler(monster, traveler, random, log);
            }
            else
            {
                AttackTraveler(monster, traveler, random, log);
                if (traveler.Health.IsDead)
                {
                    break;
                }

                AttackMonster(traveler, monster, random, log);
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
                // A full pack (docs/GDD.md §7's abstract, off-grid NPC
                // grind has no floor to leave loot on) just loses it —
                // the caller still gets the full `loot` list back
                // regardless, since a caller with a real floor (the
                // shared-server player fight, Game/Commands.cs's Fight)
                // always moves everything to the ground anyway rather
                // than relying on this add having actually happened.
                if (traveler.AddToInventory(item))
                {
                    log.Add($"{traveler.Name} looted {item.Name}.");
                }
                else
                {
                    log.Add($"{traveler.Name}'s pack is full — {item.Name} is left behind.");
                }
            }
        }

        return loot;
    }

    /// <summary>
    /// Rolls one attack's damage with the standard variance band — shared
    /// with CombatSession. Mitigation is ratio-based
    /// (<c>attack² / (attack + defense)</c>, a common diminishing-returns
    /// armour curve) rather than a linear subtraction: full attack lands
    /// when defence is 0, half lands when defence equals attack, and it
    /// keeps falling — smoothly, never to a hard floor or to 0 — as
    /// defence climbs further past it. This replaced a subtract-then-clamp
    /// formula (<c>max(attack - defense, 0.30 × attack)</c>) whose floor
    /// was meant to be a rare safety net for an over-armoured late-game
    /// player but, because defence and attack are tuned on separate
    /// numeric scales (see <see cref="ChronoTravelers.Core.Monsters.MonsterScaling"/>'s
    /// doc comment), turned out to be the permanent state of almost every
    /// fight at every tier — a flat 2-4 damage regardless of how deep the
    /// timeline got. A ratio stays meaningful at any scale, so it doesn't
    /// need its own threshold to keep in sync with whatever the attack/
    /// defense curves are doing.
    /// </summary>
    internal static int RollDamage(int attackerPower, int defenderDefense, IRandomSource random)
    {
        var attack = Math.Max(1, attackerPower);
        var defense = Math.Max(0, defenderDefense);
        var raw = attack * (double)attack / (attack + defense);
        var varianceFactor = 1 - DamageVariance + random.NextDouble() * (2 * DamageVariance);
        return Math.Max(1, (int)Math.Round(raw * varianceFactor));
    }

    /// <summary>Traveler-attacks-monster half of a round — folds in every passive that depends on knowing the target (docs/GDD.md §4.2.1: Spy "Opportunist", Scientist "Field Calibration") and records the hit for Soldier "Juggernaut Momentum".</summary>
    private static void AttackMonster(Traveler traveler, Monster monster, IRandomSource random, List<string> log)
    {
        var rawDamage = RollDamage(traveler.EffectiveAttackPower, monster.Defense, random);
        var multiplier = traveler.AttackDamageMultiplierAgainst(monster);
        var damage = multiplier == 1.0 ? rawDamage : Math.Max(1, (int)Math.Round(rawDamage * multiplier));
        var actualDamage = monster.Health.Damage(damage);
        traveler.RecordAttackLanded();
        log.Add($"{traveler.Name} hits {monster.Name} for {actualDamage} damage.");
    }

    /// <summary>Monster-attacks-traveler half of a round — routes the hit through <see cref="Traveler.TakeDamage"/> so Second Wind/Resonant Calm/Unbreakable (docs/GDD.md §4.2.1) apply exactly like every other damage-application site.</summary>
    private static void AttackTraveler(Monster monster, Traveler traveler, IRandomSource random, List<string> log)
    {
        var damage = RollDamage(monster.EffectiveAttackPower, traveler.EffectiveDefense, random);
        var actualDamage = traveler.TakeDamage(damage, attackerIsEcho: monster.HasTag("echo"));
        log.Add($"{monster.Name} hits {traveler.Name} for {actualDamage} damage.");
    }
}
