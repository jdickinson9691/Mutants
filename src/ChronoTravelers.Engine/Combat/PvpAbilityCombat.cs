using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Monsters;
using ChronoTravelers.Engine.Content;

namespace ChronoTravelers.Engine.Combat;

/// <summary>
/// The ability-casting, ranged-opening-shot counterpart to
/// <see cref="CombatResolver.FightTraveler"/> — closes the beta-review gap
/// that PvP (docs/GDD.md §11) was melee/basic-attack-only on both sides
/// while every other fight in the game (interactively, via
/// <see cref="CombatSession"/>, or abstractly, via
/// <see cref="Npc.NpcController"/>'s own AI) lets a class actually use its
/// kit. <see cref="CombatSession"/> itself is Monster-coupled throughout
/// (buff/debuff fields, <c>HasTag</c> checks, its own per-fight state) and
/// PvP still auto-resolves in one call rather than an interactive round loop
/// (docs/SERVER.md: no line-protocol round to prompt either side on), so
/// this is a new, symmetric engine rather than a retrofit of CombatSession —
/// both sides get an AI-driven ability chooser, ported from
/// <see cref="Npc.NpcController"/>'s own heuristic (<c>ChooseAbility</c>/
/// <c>ScoreAbility</c>) but re-targeted at an opposing Traveler's HP instead
/// of a Monster's, since PvP has no human to prompt on either end regardless
/// of which side is the actual player.
///
/// <see cref="AbilityEffectType.InstantDefeatNonBoss"/> is left out of the
/// usable set entirely — an outright banish would make a duel a coin flip
/// on who drew it first, the opposite of this fix's point — along with
/// <see cref="AbilityEffectType.ShortTeleport"/>/<see cref="AbilityEffectType.ReviveAlly"/>
/// (overworld-only, same refusal <see cref="CombatSession.Cast"/> already
/// makes mid-fight).
/// </summary>
public static class PvpAbilityCombat
{
    /// <summary>Safety valve mirroring <see cref="CombatResolver.Fight"/>'s own — see that constant's doc comment.</summary>
    private const int MaxRounds = 200;

    /// <summary>Same rationing heuristic as <see cref="Npc.NpcController"/>'s identical constant — see its doc comment.</summary>
    private const double AbilityCastChance = 0.6;

    /// <summary>Same self-preservation threshold as <see cref="Npc.NpcController"/>'s identical constant.</summary>
    private const double EmergencyHealHpFraction = 0.4;

    private static readonly HashSet<AbilityEffectType> ExcludedFromPvp =
    [
        AbilityEffectType.None,
        AbilityEffectType.InstantDefeatNonBoss,
        AbilityEffectType.ShortTeleport,
        AbilityEffectType.ReviveAlly,
    ];

    /// <summary>
    /// Fights <paramref name="attacker"/> against <paramref name="defender"/>
    /// with both sides' class abilities (filtered from the full
    /// <paramref name="abilities"/> catalog by each Traveler's own Class/
    /// Level, same gating <see cref="CombatSession.Cast"/> enforces) in
    /// play, plus one opening ranged shot each for a readied, loaded ranged
    /// weapon (<see cref="RangedResolver.FireAtTraveler"/>) before melee
    /// begins. Reward/loot shape (winner's XP/Credits off the loser's
    /// level-derived tier, loser's whole inventory dropped) is unchanged
    /// from <see cref="CombatResolver.FightTraveler"/> — only how the fight
    /// itself plays out changed.
    /// </summary>
    public static TravelerFightResult Fight(Traveler attacker, Traveler defender, IReadOnlyList<AbilityData> abilities, IRandomSource random)
    {
        var log = new List<string>();
        attacker.ResetPerFightState();
        defender.ResetPerFightState();

        var a = new Side(attacker, UsableAbilities(attacker, abilities));
        var d = new Side(defender, UsableAbilities(defender, abilities));

        OpeningShot(a, d, random, log);
        if (!attacker.Health.IsDead && !defender.Health.IsDead)
        {
            OpeningShot(d, a, random, log);
        }

        var rounds = 0;
        while (!attacker.Health.IsDead && !defender.Health.IsDead && rounds < MaxRounds)
        {
            rounds++;

            var attackerFirst = a.EffectiveSpeed >= d.EffectiveSpeed;
            if (attackerFirst)
            {
                TakeTurn(a, d, random, log);
                if (!defender.Health.IsDead)
                {
                    TakeTurn(d, a, random, log);
                }
            }
            else
            {
                TakeTurn(d, a, random, log);
                if (!attacker.Health.IsDead)
                {
                    TakeTurn(a, d, random, log);
                }
            }

            if (!attacker.Health.IsDead && !defender.Health.IsDead)
            {
                TickDot(a, log);
                if (!attacker.Health.IsDead)
                {
                    TickDot(d, log);
                }
            }
        }

        var attackerWon = defender.Health.IsDead && !attacker.Health.IsDead;

        if (!attackerWon)
        {
            return new TravelerFightResult(AttackerWon: false, Rounds: rounds, XpAwarded: 0, CreditsAwarded: 0, ItemsDropped: [], Log: log);
        }

        // Reward/loot math identical to CombatResolver.FightTraveler — see
        // that method's doc comment for why a defender's level ÷ 10 stands
        // in for a monster tier.
        var defenderTier = Math.Max(1, (int)Math.Round(defender.Level / 10.0));
        var xpAwarded = MonsterScaling.KillXp(MonsterScaling.XpReward(defenderTier), defenderTier, attacker.Level);
        var creditsAwarded = MonsterScaling.KillCredits(MonsterScaling.CreditReward(defenderTier), defenderTier, attacker.Level);

        var levelsGained = attacker.GainXp(xpAwarded);
        if (levelsGained > 0)
        {
            log.Add($"{attacker.Name} gained {levelsGained} level(s)!");
        }

        attacker.AddCredits(creditsAwarded);

        var loot = defender.Inventory.ToList();

        return new TravelerFightResult(AttackerWon: true, Rounds: rounds, XpAwarded: xpAwarded, CreditsAwarded: creditsAwarded, ItemsDropped: loot, Log: log);
    }

    private static List<AbilityData> UsableAbilities(Traveler traveler, IReadOnlyList<AbilityData> abilities) =>
        abilities.Where(ab => string.Equals(ab.Class, traveler.Class.ToString(), StringComparison.OrdinalIgnoreCase)
            && ab.Level <= traveler.Level
            && Enum.TryParse<AbilityEffectType>(ab.Effect, ignoreCase: true, out var effect)
            && !ExcludedFromPvp.Contains(effect))
        .ToList();

    private static void OpeningShot(Side shooter, Side target, IRandomSource random, List<string> log)
    {
        var weapon = shooter.Traveler.EquippedRanged;
        if (weapon is null || weapon.IsDepleted)
        {
            return;
        }

        var result = RangedResolver.FireAtTraveler(shooter.Traveler, target.Traveler, weapon, random);
        log.Add(result.Message);
    }

    private static void TakeTurn(Side self, Side opponent, IRandomSource random, List<string> log)
    {
        if (self.Traveler.Health.IsDead || opponent.Traveler.Health.IsDead)
        {
            return;
        }

        var chosen = self.Abilities.Count > 0 ? ChooseAbility(self, opponent, random) : null;
        if (chosen is null || !TryCast(self, opponent, chosen, random, log))
        {
            PerformAttack(self, opponent, random, log);
        }
    }

    /// <summary>Direct port of <see cref="Npc.NpcController"/>'s <c>ChooseAbility</c>, retargeted at an opposing Traveler's HP instead of a Monster's — see that method's doc comment for the self-preservation/rationing shape.</summary>
    private static AbilityData? ChooseAbility(Side self, Side opponent, IRandomSource random)
    {
        var hpFraction = self.Traveler.Health.Max > 0 ? self.Traveler.Health.Current / (double)self.Traveler.Health.Max : 1.0;
        var isEmergency = hpFraction < EmergencyHealHpFraction;

        if (!isEmergency && random.NextDouble() >= AbilityCastChance)
        {
            return null;
        }

        AbilityData? best = null;
        var bestScore = 0.0;

        foreach (var ability in self.Abilities)
        {
            if (!self.Traveler.Tachyons.CanAfford(ability.TachyonCost))
            {
                continue;
            }

            var score = ScoreAbility(ability, self, opponent, hpFraction);
            if (score > bestScore)
            {
                bestScore = score;
                best = ability;
            }
        }

        return best;
    }

    /// <summary>Same heuristic as <see cref="Npc.NpcController"/>'s <c>ScoreAbility</c>, minus the Monster-tag "TargetTagged" condition (a Traveler has no tags to check) — see that method's doc comment for the reasoning behind each weight.</summary>
    private static double ScoreAbility(AbilityData ability, Side self, Side opponent, double hpFraction)
    {
        if (!Enum.TryParse<AbilityEffectType>(ability.Effect, ignoreCase: true, out var effect))
        {
            return -1;
        }

        var conditionMet = ability.Condition switch
        {
            "TargetUndamaged" => opponent.Traveler.Health.Current == opponent.Traveler.Health.Max,
            "TargetBelow25Percent" => opponent.Traveler.Health.Current <= opponent.Traveler.Health.Max * 0.25,
            _ => false,
        };
        var conditionBonus = !string.IsNullOrEmpty(ability.Condition) && conditionMet ? 5.0 : 0.0;

        return effect switch
        {
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
            AbilityEffectType.RestoreTachyons => self.Traveler.Tachyons.Current < self.Traveler.Tachyons.Max * 0.3 ? 15 : 0,
            _ => -1,
        };
    }

    private static bool TryCast(Side self, Side opponent, AbilityData ability, IRandomSource random, List<string> log)
    {
        if (!Enum.TryParse<AbilityEffectType>(ability.Effect, ignoreCase: true, out var effect) || ExcludedFromPvp.Contains(effect))
        {
            return false;
        }

        // Scientist "Stable Core" free-cast roll, same as CombatSession.Cast.
        var freeCast = self.Traveler.FreeCastChance > 0 && random.NextDouble() < self.Traveler.FreeCastChance;
        var cost = freeCast ? 0 : self.Traveler.EffectiveCastCost(ability.TachyonCost);
        if (!self.Traveler.Tachyons.CanAfford(cost))
        {
            return false;
        }

        if (cost > 0)
        {
            self.Traveler.Tachyons.Spend(cost);
        }

        log.Add(freeCast ? $"{self.Traveler.Name} casts {ability.Name} at no cost!" : $"{self.Traveler.Name} casts {ability.Name}!");
        ApplyEffect(self, opponent, ability, effect, random, log);
        return true;
    }

    /// <summary>Mirrors <see cref="CombatSession"/>'s <c>TravelerTurn</c> switch, retargeted so either side can be "self" and the other "opponent" — see that method for the original, Monster-coupled version of each case.</summary>
    private static void ApplyEffect(Side self, Side opponent, AbilityData ability, AbilityEffectType effect, IRandomSource random, List<string> log)
    {
        switch (effect)
        {
            case AbilityEffectType.Damage:
                var conditionMet = ability.Condition switch
                {
                    "TargetUndamaged" => opponent.Traveler.Health.Current == opponent.Traveler.Health.Max,
                    "TargetBelow25Percent" => opponent.Traveler.Health.Current <= opponent.Traveler.Health.Max * 0.25,
                    _ => true, // no condition, or one this engine can't evaluate against a Traveler — fall back to a plain hit rather than fizzling, same leniency CombatSession.PerformTravelerAttack applies.
                };
                PerformAttack(self, opponent, random, log, conditionMet ? ability.Magnitude : 1.0);
                break;

            case AbilityEffectType.IgnoreDefenseDamage:
                PerformAttack(self, opponent, random, log, ability.Magnitude, ignoreDefense: true);
                break;

            case AbilityEffectType.Heal:
                var healed = self.Traveler.Health.Heal((int)Math.Round(self.Traveler.Health.Max * ability.Magnitude));
                log.Add($"{self.Traveler.Name} heals for {healed} HP.");
                break;

            case AbilityEffectType.BuffSelfAttack:
                self.AttackBonus += (int)ability.Magnitude;
                log.Add($"{self.Traveler.Name}'s attack is bolstered.");
                break;

            case AbilityEffectType.BuffSelfDefense:
                self.DefenseBonus += (int)ability.Magnitude;
                log.Add($"{self.Traveler.Name}'s defense is bolstered.");
                break;

            case AbilityEffectType.DebuffTargetAttack:
                opponent.IncomingAttackPenalty += (int)ability.Magnitude;
                log.Add($"{opponent.Traveler.Name}'s attack is weakened.");
                break;

            case AbilityEffectType.DebuffTargetDefense:
                opponent.IncomingDefensePenalty += (int)ability.Magnitude;
                log.Add($"{opponent.Traveler.Name}'s defenses are cracked open.");
                break;

            case AbilityEffectType.DebuffTargetSpeed:
                opponent.SpeedPenalty += (int)ability.Magnitude;
                log.Add($"{opponent.Traveler.Name} is slowed.");
                break;

            case AbilityEffectType.GuaranteedCritNextAttack:
                self.CritCharge = true;
                self.CritMultiplier = ability.Magnitude;
                log.Add($"{self.Traveler.Name} vanishes into the shadows...");
                break;

            case AbilityEffectType.ExtraAttack:
                PerformAttack(self, opponent, random, log);
                if (!opponent.Traveler.Health.IsDead)
                {
                    PerformAttack(self, opponent, random, log);
                }

                break;

            case AbilityEffectType.Shield:
                self.ShieldCharge = true;
                log.Add($"{self.Traveler.Name} is shielded.");
                break;

            case AbilityEffectType.DamageOverTime:
                PerformAttack(self, opponent, random, log);
                opponent.DotDamagePerRound = (int)ability.Magnitude;
                opponent.DotRoundsRemaining = ability.DurationRounds;
                log.Add($"{opponent.Traveler.Name} is poisoned.");
                break;

            case AbilityEffectType.RestoreTachyons:
                var restored = self.Traveler.Tachyons.Add((int)Math.Round(self.Traveler.Tachyons.Max * ability.Magnitude));
                log.Add($"{self.Traveler.Name} restores {restored} Tachyons.");
                break;
        }
    }

    /// <summary>
    /// One attack, either a plain hit or an ability-enhanced one. Shield is
    /// checked before crit consumption so a charged crit isn't wasted on a
    /// hit that was going to be fully absorbed anyway — a small departure
    /// from <see cref="CombatSession"/>, which never has to make that call
    /// since shield only ever protects the player there and crit only ever
    /// belongs to the player too, so the two never interact. Here either
    /// side can hold either charge, so the order is a deliberate judgment
    /// call, not copied from anywhere.
    /// </summary>
    private static void PerformAttack(Side self, Side opponent, IRandomSource random, List<string> log, double damageMultiplier = 1.0, bool ignoreDefense = false)
    {
        if (self.Traveler.Health.IsDead || opponent.Traveler.Health.IsDead)
        {
            return;
        }

        if (opponent.ShieldCharge)
        {
            opponent.ShieldCharge = false;
            log.Add($"{opponent.Traveler.Name}'s attack is absorbed by a shield!");
            return;
        }

        if (self.CritCharge)
        {
            damageMultiplier *= self.CritMultiplier;
            self.CritCharge = false;
            log.Add($"{self.Traveler.Name} strikes from the shadows!");
        }

        var defense = ignoreDefense ? 0 : opponent.EffectiveDefense;
        var baseDamage = CombatResolver.RollDamage(self.EffectiveAttack, defense, random);
        var damage = Math.Max(1, (int)Math.Round(baseDamage * damageMultiplier));
        var actualDamage = opponent.Traveler.TakeDamage(damage);
        self.Traveler.RecordAttackLanded();
        self.Traveler.DegradeEquippedWeapon(); // docs/GDD.md §6.3's "repair costs" Credit sink — see Traveler.DegradeEquippedWeapon.
        opponent.Traveler.DegradeEquippedArmor(); // same, for the side taking the hit.
        log.Add($"{self.Traveler.Name} hits {opponent.Traveler.Name} for {actualDamage} damage.");
    }

    private static void TickDot(Side side, List<string> log)
    {
        if (side.DotRoundsRemaining <= 0 || side.Traveler.Health.IsDead)
        {
            return;
        }

        var dotDamage = side.Traveler.TakeDamage(side.DotDamagePerRound);
        log.Add($"{side.Traveler.Name} takes {dotDamage} poison damage.");
        side.DotRoundsRemaining--;
        if (side.Traveler.Health.IsDead)
        {
            log.Add($"{side.Traveler.Name} succumbs to the poison.");
        }
    }

    /// <summary>
    /// Per-side, fight-duration ability state — the PvP counterpart to
    /// <see cref="CombatSession"/>'s private fields, just doubled up (one
    /// instance per combatant instead of one hardcoded to "the traveler")
    /// since either side here can buff itself or debuff the other.
    /// </summary>
    private sealed class Side
    {
        public Side(Traveler traveler, IReadOnlyList<AbilityData> abilities)
        {
            Traveler = traveler;
            Abilities = abilities;
        }

        public Traveler Traveler { get; }
        public IReadOnlyList<AbilityData> Abilities { get; }

        public int AttackBonus;
        public int DefenseBonus;
        public int IncomingAttackPenalty;
        public int IncomingDefensePenalty;
        public int SpeedPenalty;
        public bool ShieldCharge;
        public bool CritCharge;
        public double CritMultiplier = 1.0;
        public int DotDamagePerRound;
        public int DotRoundsRemaining;

        public int EffectiveAttack => Traveler.EffectiveAttackPower + AttackBonus - IncomingAttackPenalty;
        public int EffectiveDefense => Math.Max(0, Traveler.EffectiveDefense + DefenseBonus - IncomingDefensePenalty);
        public int EffectiveSpeed => Math.Max(0, Traveler.Speed - SpeedPenalty);
    }
}
