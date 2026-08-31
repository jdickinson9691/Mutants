using ChronTravelers.Core.Characters;
using ChronTravelers.Core.Classes;
using ChronTravelers.Core.Items;
using ChronTravelers.Core.Monsters;
using ChronTravelers.Engine.Content;

namespace ChronTravelers.Engine.Combat;

/// <summary>
/// An interactive, round-by-round fight — the player chooses each round
/// to make a normal attack (<see cref="Attack"/>) or cast an ability
/// (<see cref="Cast"/>), unlike <see cref="CombatResolver.Fight"/>'s
/// instant, fully-automated resolution (still used as-is for NPC auto-
/// combat and gatekeeper fights - see ChronTravelers.Console's file header for
/// what's wired to which). Buffs/debuffs from abilities last for the
/// rest of the fight rather than a precise round countdown (except
/// Poison Blade's DamageOverTime, which genuinely needs one) — a
/// deliberate simplification given how short fights in this game are;
/// original design, not GDD-specified.
/// </summary>
public sealed class CombatSession
{
    public Mutant Mutant { get; }
    public Monster Monster { get; }

    /// <summary>False during a gatekeeper fight, where Banish shouldn't be able to trivially skip the level's boss.</summary>
    public bool AllowBanish { get; }

    public int Rounds { get; private set; }
    public IReadOnlyList<string> Log => _log;
    public bool IsOver => Mutant.Health.IsDead || Monster.Health.IsDead;
    public bool MutantWon => Monster.Health.IsDead && !Mutant.Health.IsDead;
    public int XpAwarded { get; private set; }
    public IReadOnlyList<Item> ItemsDropped { get; private set; } = [];

    private readonly IRandomSource _random;
    private readonly List<string> _log = [];
    private bool _rewardsGranted;

    // Ability-driven combat-duration state - see the class doc comment
    // for why these last "the rest of the fight" rather than N rounds.
    private int _mutantAttackBonus;
    private int _mutantDefenseBonus;
    private int _monsterAttackPenalty;
    private int _monsterDefensePenalty;
    private int _monsterSpeedPenalty;
    private bool _shieldCharge;
    private bool _critCharge;
    private double _critMultiplier = 1.0;
    private int _dotDamagePerRound;
    private int _dotRoundsRemaining;

    public CombatSession(Mutant mutant, Monster monster, IRandomSource random, bool allowBanish = true)
    {
        Mutant = mutant;
        Monster = monster;
        _random = random;
        AllowBanish = allowBanish;

        // A ranged Weaken shot landed before this fight — apply it once,
        // then spend it (see ChronTravelers.Engine.Combat.RangedResolver).
        if (monster.PendingDefensePenalty > 0)
        {
            _monsterDefensePenalty += monster.PendingDefensePenalty;
            monster.PendingDefensePenalty = 0;
            _log.Add($"{monster.Name} is still reeling from the shot — its guard is down.");
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
    /// Ion cost first, refunding nothing spent on failure. An ability
    /// whose Effect is "None" (see AbilityData) is refused with no Ion
    /// cost, rather than silently doing nothing.
    /// </summary>
    public AbilityCastResult Cast(AbilityData ability)
    {
        if (IsOver)
        {
            return new AbilityCastResult(false, "The fight is already over.");
        }

        if (!Enum.TryParse<CharacterClass>(ability.Class, ignoreCase: true, out var abilityClass) || abilityClass != Mutant.Class)
        {
            return new AbilityCastResult(false, $"{Mutant.Name} can't use {ability.Name} — that's a {ability.Class} ability.");
        }

        if (Mutant.Level < ability.Level)
        {
            return new AbilityCastResult(false, $"{ability.Name} unlocks at level {ability.Level} (you're level {Mutant.Level}).");
        }

        if (!Enum.TryParse<AbilityEffectType>(ability.Effect, ignoreCase: true, out var effectType))
        {
            effectType = AbilityEffectType.None;
        }

        if (effectType == AbilityEffectType.None)
        {
            return new AbilityCastResult(false, $"{ability.Name} has no effect in combat yet — it's a passive/party/overworld mechanic this engine doesn't model. No Ions spent.");
        }

        if (effectType == AbilityEffectType.InstantDefeatNonBoss && !AllowBanish)
        {
            return new AbilityCastResult(false, $"{ability.Name} has no effect against a gatekeeper. No Ions spent.");
        }

        if (!Mutant.Ions.CanAfford(ability.IonCost))
        {
            return new AbilityCastResult(false, $"Not enough Ions ({ability.IonCost} needed; you have {Mutant.Ions.Current}).");
        }

        Mutant.Ions.Spend(ability.IonCost);
        ResolveRound(ability, effectType);
        return new AbilityCastResult(true, $"{Mutant.Name} casts {ability.Name}!");
    }

    private void ResolveRound(AbilityData? ability, AbilityEffectType effectType)
    {
        Rounds++;

        var mutantActsFirst = Mutant.Speed >= MonsterEffectiveSpeed();

        if (mutantActsFirst)
        {
            MutantTurn(ability, effectType);
            if (!Monster.Health.IsDead)
            {
                MonsterTurn();
            }
        }
        else
        {
            MonsterTurn();
            if (!Mutant.Health.IsDead)
            {
                MutantTurn(ability, effectType);
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

        if (IsOver && MutantWon && !_rewardsGranted)
        {
            _rewardsGranted = true;
            XpAwarded = Monster.XpReward;
            ItemsDropped = CombatResolver.AwardVictory(Mutant, Monster, _random, _log);
        }
    }

    private void MutantTurn(AbilityData? ability, AbilityEffectType effectType)
    {
        if (ability is null)
        {
            PerformMutantAttack();
            return;
        }

        switch (effectType)
        {
            case AbilityEffectType.Damage:
                PerformMutantAttack(ability.Magnitude, condition: ability.Condition, tag: ability.Tag);
                break;

            case AbilityEffectType.IgnoreDefenseDamage:
                PerformMutantAttack(ability.Magnitude, ignoreDefense: true);
                break;

            case AbilityEffectType.Heal:
                var healed = Mutant.Health.Heal((int)Math.Round(Mutant.Health.Max * ability.Magnitude));
                _log.Add($"{Mutant.Name} heals for {healed} HP.");
                break;

            case AbilityEffectType.BuffSelfAttack:
                _mutantAttackBonus += (int)ability.Magnitude;
                _log.Add($"{Mutant.Name}'s attack is bolstered.");
                break;

            case AbilityEffectType.BuffSelfDefense:
                _mutantDefenseBonus += (int)ability.Magnitude;
                _log.Add($"{Mutant.Name}'s defense is bolstered.");
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
                _log.Add($"{Mutant.Name} vanishes into the shadows...");
                break;

            case AbilityEffectType.ExtraAttack:
                PerformMutantAttack();
                if (!Monster.Health.IsDead)
                {
                    PerformMutantAttack();
                }

                break;

            case AbilityEffectType.Shield:
                _shieldCharge = true;
                _log.Add($"{Mutant.Name} is shielded.");
                break;

            case AbilityEffectType.DamageOverTime:
                PerformMutantAttack();
                _dotDamagePerRound = (int)ability.Magnitude;
                _dotRoundsRemaining = ability.DurationRounds;
                _log.Add($"{Monster.Name} is poisoned.");
                break;

            case AbilityEffectType.RestoreIons:
                var restored = Mutant.Ions.Add((int)Math.Round(Mutant.Ions.Max * ability.Magnitude));
                _log.Add($"{Mutant.Name} restores {restored} Ions.");
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
    /// back to a plain hit rather than fizzling entirely — the Ions are
    /// already spent by the time this runs, so a total whiff would be
    /// needlessly punishing.
    /// </summary>
    private void PerformMutantAttack(double damageMultiplier = 1.0, bool ignoreDefense = false, string? condition = null, string? tag = null)
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
            _log.Add($"{Mutant.Name} strikes from the shadows!");
        }

        var defense = ignoreDefense ? 0 : MonsterEffectiveDefense();
        var baseDamage = CombatResolver.RollDamage(MutantEffectiveAttack(), defense, _random);
        var damage = Math.Max(1, (int)Math.Round(baseDamage * damageMultiplier));
        var actualDamage = Monster.Health.Damage(damage);
        _log.Add($"{Mutant.Name} hits {Monster.Name} for {actualDamage} damage.");
    }

    private void MonsterTurn()
    {
        var incoming = CombatResolver.RollDamage(MonsterEffectiveAttack(), MutantEffectiveDefense(), _random);

        if (_shieldCharge)
        {
            _shieldCharge = false;
            _log.Add($"{Monster.Name}'s attack is absorbed by a shield!");
            return;
        }

        var actualDamage = Mutant.Health.Damage(incoming);
        _log.Add($"{Monster.Name} hits {Mutant.Name} for {actualDamage} damage.");
    }

    private int MutantEffectiveAttack() => Mutant.EffectiveAttackPower + _mutantAttackBonus;

    private int MutantEffectiveDefense() => Mutant.EffectiveDefense + _mutantDefenseBonus;

    private int MonsterEffectiveAttack() => Math.Max(0, Monster.AttackPower - _monsterAttackPenalty);

    private int MonsterEffectiveDefense() => Math.Max(0, Monster.Defense - _monsterDefensePenalty);

    private int MonsterEffectiveSpeed() => Math.Max(0, Monster.Speed - _monsterSpeedPenalty);
}
