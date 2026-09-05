using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Diagnostics;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Monsters;
using ChronoTravelers.Engine.Content;

namespace ChronoTravelers.Engine.Combat;

/// <summary>
/// An interactive, round-by-round fight — the player chooses each round
/// to make a normal attack (<see cref="Attack"/>) or cast an ability
/// (<see cref="Cast"/>), unlike <see cref="CombatResolver.Fight"/>'s
/// instant, fully-automated resolution (still used as-is for NPC auto-
/// combat and warden fights - see ChronoTravelers.Console's file header for
/// what's wired to which). Buffs/debuffs from abilities last for the
/// rest of the fight rather than a precise round countdown (except
/// Poison Blade's DamageOverTime, which genuinely needs one) — a
/// deliberate simplification given how short fights in this game are;
/// original design, not GDD-specified.
/// </summary>
public sealed class CombatSession
{
    public Traveler Traveler { get; }
    public Monster Monster { get; }

    /// <summary>False during a warden fight, where Banish shouldn't be able to trivially skip the level's boss.</summary>
    public bool AllowBanish { get; }

    public int Rounds { get; private set; }
    public IReadOnlyList<string> Log => _log;
    public bool IsOver => Traveler.Health.IsDead || Monster.Health.IsDead;
    public bool TravelerWon => Monster.Health.IsDead && !Traveler.Health.IsDead;
    public int XpAwarded { get; private set; }
    public int CreditsAwarded { get; private set; }
    public IReadOnlyList<Item> ItemsDropped { get; private set; } = [];

    private readonly IRandomSource _random;
    private readonly List<string> _log = [];
    private bool _rewardsGranted;

    // Ability-driven combat-duration state - see the class doc comment
    // for why these last "the rest of the fight" rather than N rounds.
    private int _travelerAttackBonus;
    private int _travelerDefenseBonus;
    private int _monsterAttackPenalty;
    private int _monsterDefensePenalty;
    private int _monsterSpeedPenalty;
    private bool _shieldCharge;
    private bool _critCharge;
    private double _critMultiplier = 1.0;
    private int _dotDamagePerRound;
    private int _dotRoundsRemaining;

    public CombatSession(Traveler traveler, Monster monster, IRandomSource random, bool allowBanish = true)
    {
        Traveler = traveler;
        Monster = monster;
        _random = random;
        AllowBanish = allowBanish;

        // Fight-scoped passive state (Juggernaut Momentum's streak,
        // Unbreakable's once-per-fight save — docs/GDD.md §4.2.1) starts
        // fresh for this fight, same as CombatResolver.Fight.
        traveler.ResetPerFightState();

        // A ranged Weaken shot landed before this fight — apply it once,
        // then spend it (see ChronoTravelers.Engine.Combat.RangedResolver).
        if (monster.PendingDefensePenalty > 0)
        {
            _monsterDefensePenalty += monster.PendingDefensePenalty;
            monster.PendingDefensePenalty = 0;
            _log.Add($"{monster.Name} is still reeling from the shot — its guard is down.");
        }

        // Same, for a ranged Stagger shot — the offense-side counterpart.
        if (monster.PendingAttackPenalty > 0)
        {
            _monsterAttackPenalty += monster.PendingAttackPenalty;
            monster.PendingAttackPenalty = 0;
            _log.Add($"{monster.Name} is still staggered from the shot — its swings are off.");
        }
    }

    /// <summary>Makes a normal attack this round.</summary>
    public void Attack()
    {
        if (IsOver)
        {
            return;
        }

        ResolveRound(null, AbilityEffectType.None);
    }

    /// <summary>
    /// Casts an ability this round. Validates class, level-unlock, and
    /// Tachyon cost first, refunding nothing spent on failure. An ability
    /// whose Effect is "None" (see AbilityData) is refused with no Tachyon
    /// cost, rather than silently doing nothing.
    /// </summary>
    public AbilityCastResult Cast(AbilityData ability)
    {
        if (IsOver)
        {
            return new AbilityCastResult(false, "The fight is already over.");
        }

        if (!Enum.TryParse<CharacterClass>(ability.Class, ignoreCase: true, out var abilityClass) || abilityClass != Traveler.Class)
        {
            return new AbilityCastResult(false, $"{Traveler.Name} can't use {ability.Name} — that's a {ability.Class} ability.");
        }

        if (Traveler.Level < ability.Level)
        {
            return new AbilityCastResult(false, $"{ability.Name} unlocks at level {ability.Level} (you're level {Traveler.Level}).");
        }

        if (!Enum.TryParse<AbilityEffectType>(ability.Effect, ignoreCase: true, out var effectType))
        {
            effectType = AbilityEffectType.None;
        }

        if (effectType == AbilityEffectType.None)
        {
            return new AbilityCastResult(false, $"{ability.Name} has no effect in combat yet — it's a passive/party/overworld mechanic this engine doesn't model. No Tachyons spent.");
        }

        if (effectType == AbilityEffectType.InstantDefeatNonBoss && !AllowBanish)
        {
            return new AbilityCastResult(false, $"{ability.Name} has no effect against a warden. No Tachyons spent.");
        }

        // Scientist "Stable Core" — a chance the cast is free outright
        // (docs/GDD.md §4.2.1), rolled before Engineer "Failsafe
        // Capacitor"'s discount even gets a chance to apply.
        var freeCast = Traveler.FreeCastChance > 0 && _random.NextDouble() < Traveler.FreeCastChance;
        if (freeCast)
        {
            PassiveActivationTracker.Record(Traveler.Class, PassiveHook.FreeCastChancePct, ability.TachyonCost > 0 ? ability.TachyonCost : 1);
        }
        var cost = freeCast ? 0 : Traveler.EffectiveCastCost(ability.TachyonCost);

        if (!Traveler.Tachyons.CanAfford(cost))
        {
            return new AbilityCastResult(false, $"Not enough Tachyons ({cost} needed; you have {Traveler.Tachyons.Current}).");
        }

        if (cost > 0)
        {
            Traveler.Tachyons.Spend(cost);
        }

        ResolveRound(ability, effectType);
        return new AbilityCastResult(true, freeCast ? $"{Traveler.Name} casts {ability.Name} at no cost!" : $"{Traveler.Name} casts {ability.Name}!");
    }

    private void ResolveRound(AbilityData? ability, AbilityEffectType effectType)
    {
        Rounds++;

        var travelerActsFirst = Traveler.Speed >= MonsterEffectiveSpeed();

        if (travelerActsFirst)
        {
            TravelerTurn(ability, effectType);
            if (!Monster.Health.IsDead)
            {
                MonsterTurn();
            }
        }
        else
        {
            MonsterTurn();
            if (!Traveler.Health.IsDead)
            {
                TravelerTurn(ability, effectType);
            }
        }

        if (!Monster.Health.IsDead && _dotRoundsRemaining > 0)
        {
            var dotDamage = Monster.Health.Damage(_dotDamagePerRound);
            _log.Add($"{Monster.Name} takes {dotDamage} poison damage.");
            _dotRoundsRemaining--;
            if (Monster.Health.IsDead)
            {
                _log.Add($"{Monster.Name} succumbs to the poison.");
            }
        }

        if (IsOver && TravelerWon && !_rewardsGranted)
        {
            _rewardsGranted = true;
            // The player's kills drop to the ground (the console grounds
            // ItemsDropped at the player's tile); nothing auto-enters the pack.
            ItemsDropped = CombatResolver.AwardVictory(Traveler, Monster, _random, _log, out var xp, out var credits, addToInventory: false);
            XpAwarded = xp;
            CreditsAwarded = credits;
        }
    }

    private void TravelerTurn(AbilityData? ability, AbilityEffectType effectType)
    {
        if (ability is null)
        {
            PerformTravelerAttack();
            return;
        }

        switch (effectType)
        {
            case AbilityEffectType.Damage:
                PerformTravelerAttack(ability.Magnitude, condition: ability.Condition, tag: ability.Tag);
                break;

            case AbilityEffectType.IgnoreDefenseDamage:
                PerformTravelerAttack(ability.Magnitude, ignoreDefense: true);
                break;

            case AbilityEffectType.Heal:
                var healed = Traveler.Health.Heal((int)Math.Round(Traveler.Health.Max * ability.Magnitude));
                _log.Add($"{Traveler.Name} heals for {healed} HP.");
                break;

            case AbilityEffectType.BuffSelfAttack:
                _travelerAttackBonus += (int)ability.Magnitude;
                _log.Add($"{Traveler.Name}'s attack is bolstered.");
                break;

            case AbilityEffectType.BuffSelfDefense:
                _travelerDefenseBonus += (int)ability.Magnitude;
                _log.Add($"{Traveler.Name}'s defense is bolstered.");
                break;

            case AbilityEffectType.DebuffTargetAttack:
                _monsterAttackPenalty += (int)ability.Magnitude;
                _log.Add($"{Monster.Name}'s attack is weakened.");
                break;

            case AbilityEffectType.DebuffTargetDefense:
                _monsterDefensePenalty += (int)ability.Magnitude;
                _log.Add($"{Monster.Name}'s defenses are cracked open.");
                break;

            case AbilityEffectType.DebuffTargetSpeed:
                _monsterSpeedPenalty += (int)ability.Magnitude;
                _log.Add($"{Monster.Name} is slowed.");
                break;

            case AbilityEffectType.GuaranteedCritNextAttack:
                _critCharge = true;
                _critMultiplier = ability.Magnitude;
                _log.Add($"{Traveler.Name} vanishes into the shadows...");
                break;

            case AbilityEffectType.ExtraAttack:
                PerformTravelerAttack();
                if (!Monster.Health.IsDead)
                {
                    PerformTravelerAttack();
                }

                break;

            case AbilityEffectType.Shield:
                _shieldCharge = true;
                _log.Add($"{Traveler.Name} is shielded.");
                break;

            case AbilityEffectType.DamageOverTime:
                PerformTravelerAttack();
                _dotDamagePerRound = (int)ability.Magnitude;
                _dotRoundsRemaining = ability.DurationRounds;
                _log.Add($"{Monster.Name} is poisoned.");
                break;

            case AbilityEffectType.RestoreTachyons:
                var restored = Traveler.Tachyons.Add((int)Math.Round(Traveler.Tachyons.Max * ability.Magnitude));
                _log.Add($"{Traveler.Name} restores {restored} Tachyons.");
                break;

            case AbilityEffectType.InstantDefeatNonBoss:
                Monster.Health.Damage(Monster.Health.Current);
                _log.Add($"{Monster.Name} is banished from the fight.");
                break;
        }
    }

    /// <summary>
    /// A normal (or ability-enhanced) attack. If <paramref name="condition"/>
    /// isn't met, the ability's bonus multiplier is dropped and this falls
    /// back to a plain hit rather than fizzling entirely — the Tachyons are
    /// already spent by the time this runs, so a total whiff would be
    /// needlessly punishing.
    /// </summary>
    private void PerformTravelerAttack(double damageMultiplier = 1.0, bool ignoreDefense = false, string? condition = null, string? tag = null)
    {
        if (!string.IsNullOrEmpty(condition))
        {
            var conditionMet = condition switch
            {
                "TargetUndamaged" => Monster.Health.Current == Monster.Health.Max,
                "TargetBelow25Percent" => Monster.Health.Current <= Monster.Health.Max * 0.25,
                "TargetTagged" => tag is not null && Monster.HasTag(tag),
                _ => true,
            };

            if (!conditionMet)
            {
                damageMultiplier = 1.0;
            }
        }

        if (_critCharge)
        {
            damageMultiplier *= _critMultiplier;
            _critCharge = false;
            _log.Add($"{Traveler.Name} strikes from the shadows!");
        }

        var defense = ignoreDefense ? 0 : MonsterEffectiveDefense();
        var baseDamage = CombatResolver.RollDamage(TravelerEffectiveAttack(), defense, _random);
        var passiveMultiplier = Traveler.AttackDamageMultiplierAgainst(Monster);
        var damage = Math.Max(1, (int)Math.Round(baseDamage * damageMultiplier * passiveMultiplier));
        var actualDamage = Monster.Health.Damage(damage);
        Traveler.RecordAttackLanded();
        _log.Add($"{Traveler.Name} hits {Monster.Name} for {actualDamage} damage.");
    }

    private void MonsterTurn()
    {
        var incoming = CombatResolver.RollDamage(MonsterEffectiveAttack(), TravelerEffectiveDefense(), _random);

        if (_shieldCharge)
        {
            _shieldCharge = false;
            _log.Add($"{Monster.Name}'s attack is absorbed by a shield!");
            return;
        }

        var actualDamage = Traveler.TakeDamage(incoming, attackerIsEcho: Monster.HasTag("echo"));
        _log.Add($"{Monster.Name} hits {Traveler.Name} for {actualDamage} damage.");
    }

    private int TravelerEffectiveAttack() => Traveler.EffectiveAttackPower + _travelerAttackBonus;

    private int TravelerEffectiveDefense() => Traveler.EffectiveDefense + _travelerDefenseBonus;

    private int MonsterEffectiveAttack() => Math.Max(0, Monster.EffectiveAttackPower - _monsterAttackPenalty);

    private int MonsterEffectiveDefense() => Math.Max(0, Monster.Defense - _monsterDefensePenalty);

    private int MonsterEffectiveSpeed() => Math.Max(0, Monster.Speed - _monsterSpeedPenalty);
}
