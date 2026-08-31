using Mutants.Core.Characters;
using Mutants.Core.Items;
using Mutants.Core.Monsters;

namespace Mutants.Engine.Combat;

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
    public static RangedResult Fire(Mutant shooter, Monster target, Item weapon, IRandomSource random)
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
        var weaponBonus = (int)Math.Round(weapon.AttackBonus * weapon.WieldEffectiveness(shooter.Class));
        var attack = shooter.Stats.Get(shooter.ClassDefinition.PrimaryStat) + weaponBonus;

        var pierces = weapon.RangedKind is RangedKind.Wand or RangedKind.Gun;
        var raw = CombatResolver.RollDamage(attack, pierces ? 0 : target.Defense, random);
        var magnitude = weapon.EffectMagnitude > 0 ? weapon.EffectMagnitude : 1.0;
        var damage = Math.Max(1, (int)Math.Round(raw * magnitude));

        var dealt = target.Health.Damage(damage);
        var killed = target.Health.IsDead;

        var effectNote = "";
        if (!killed && weapon.RangedEffect == RangedEffectType.Weaken)
        {
            var weaken = Math.Max(1, (int)Math.Round(magnitude));
            target.PendingDefensePenalty += weaken;
            effectNote = $" {target.Name}'s guard is rattled (-{weaken} defense next fight).";
        }

        var verb = weapon.RangedKind == RangedKind.Wand ? "blasts" : "hits";
        var message = killed
            ? $"Your {weapon.Name} {verb} {target.Name} for {dealt} — it drops."
            : $"Your {weapon.Name} {verb} {target.Name} for {dealt} damage.{effectNote}";

        return new RangedResult(dealt, killed, message);
    }
}
