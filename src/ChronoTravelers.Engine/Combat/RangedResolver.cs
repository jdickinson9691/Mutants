using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Monsters;

namespace ChronoTravelers.Engine.Combat;

/// <summary>
/// Resolves one shot from a ranged weapon (<c>point</c> / <c>shoot</c>) at
/// a monster one room away — the ranged counterpart to
/// <see cref="CombatResolver"/>. A shot spends one round of the weapon's
/// built-in ammo, deals a single hit (Wands and Guns pierce armour, Bows
/// don't), and may apply the weapon's <see cref="RangedEffectType"/>. XP
/// and loot on a kill are the caller's job (they depend on where the
/// target is standing).
/// </summary>
public static class RangedResolver
{
    public static RangedResult Fire(Traveler shooter, Monster target, Item weapon, IRandomSource random)
    {
        if (!weapon.IsRanged)
        {
            throw new InvalidOperationException($"'{weapon.Name}' is not a ranged weapon.");
        }

        if (weapon.IsDepleted)
        {
            throw new InvalidOperationException($"'{weapon.Name}' has no shots left.");
        }

        weapon.AmmoRemaining--;

        // Ranged damage = primary stat + the ranged weapon's own bonus
        // (class-fit scaled) x its magnitude — deliberately NOT the melee
        // weapon bonus or potion buffs, which belong to melee attacks.
        var offClassPenaltyReduction = Core.Characters.PassiveTraits.Sum(shooter.Class, shooter.Level, Core.Characters.PassiveHook.OffClassPenaltyReductionPct);
        var weaponBonus = (int)Math.Round(weapon.AttackBonus * weapon.WieldEffectiveness(shooter.Class, offClassPenaltyReduction));
        var attack = shooter.Stats.Get(shooter.ClassDefinition.PrimaryStat) + weaponBonus;

        var pierces = weapon.RangedKind is RangedKind.Wand or RangedKind.Gun;
        var raw = CombatResolver.RollDamage(attack, pierces ? 0 : target.Defense, random);
        var magnitude = weapon.EffectMagnitude > 0 ? weapon.EffectMagnitude : 1.0;
        // Spy "Opportunist" / Scientist "Field Calibration" (docs/GDD.md
        // §4.2.1) apply to ranged shots too — same target-aware multiplier
        // as a melee hit.
        var passiveMultiplier = shooter.AttackDamageMultiplierAgainst(target);
        var damage = Math.Max(1, (int)Math.Round(raw * magnitude * passiveMultiplier));

        var dealt = target.Health.Damage(damage);
        var killed = target.Health.IsDead;

        var effectNote = "";
        if (!killed)
        {
            target.RaiseAggro(ChronoTravelers.Core.Monsters.AggroModel.RangedHitAggro); // you shot it — it noticed

            // Fight, pursue, or flee — see Monster.IsPursuing/IsFleeing and
            // MonsterController's per-tick handling of both. Sharing the
            // shooter's room (the defensive case; the normal 'fight'-then-
            // 'fire' flow always shoots from a room away) means straight to
            // Hostile — no use running with the target already toe to toe.
            if (shooter.Position.Equals(target.Position))
            {
                target.RaiseAggro(AggroModel.Cap);
                target.IsPursuing = false;
                target.IsFleeing = false;
            }
            else
            {
                var hpFraction = target.Health.Max > 0 ? target.Health.Current / (double)target.Health.Max : 0;
                if (target.FleeBelowHpFraction > 0 && hpFraction <= target.FleeBelowHpFraction)
                {
                    target.IsFleeing = true;
                    target.IsPursuing = false;
                }
                else
                {
                    target.IsPursuing = true;
                    target.IsFleeing = false;
                    target.PursuitTicksRemaining = Monster.MaxPursuitTicks;
                }
            }

            if (weapon.RangedEffect == RangedEffectType.Weaken)
            {
                var weaken = Math.Max(1, (int)Math.Round(magnitude));
                target.PendingDefensePenalty += weaken;
                effectNote = $" {target.Name}'s guard is rattled (-{weaken} defense next fight).";
            }
            else if (weapon.RangedEffect == RangedEffectType.Stagger)
            {
                var stagger = Math.Max(1, (int)Math.Round(magnitude));
                target.PendingAttackPenalty += stagger;
                effectNote = $" {target.Name} is staggered (-{stagger} attack next fight).";
            }
        }

        var verb = weapon.RangedKind == RangedKind.Wand ? "blasts" : "hits";
        var message = killed
            ? $"Your {weapon.Name} {verb} {target.Name} for {dealt} — it drops."
            : $"Your {weapon.Name} {verb} {target.Name} for {dealt} damage.{effectNote}";

        return new RangedResult(dealt, killed, message);
    }
}
