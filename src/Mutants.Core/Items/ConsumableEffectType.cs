namespace Mutants.Core.Items;

/// <summary>
/// What a Consumable item does when used — docs/GDD.md §5's wield/sell/
/// convert three-way choice gets a fourth option for this ItemType:
/// use/eat/drink it. Deliberately a smaller taxonomy than
/// Mutants.Engine.Combat.AbilityEffectType: everything here is
/// self-directed and usable outside combat too (there's no "target," so
/// no Damage/debuff-style effects), and this type has to live in
/// Mutants.Core (Item's own namespace) rather than alongside abilities in
/// Mutants.Engine, since Core doesn't reference Engine.
/// </summary>
public enum ConsumableEffectType
{
    /// <summary>Not consumable — every non-Consumable item, and any Consumable that's flavor-only.</summary>
    None,

    /// <summary>Instantly restores Magnitude HP, flat (not a fraction of max HP) — "food" items use this.</summary>
    Heal,

    /// <summary>Temporary flat bonus to attack power for DurationTicks ticks — a "stat increase" potion.</summary>
    BuffAttack,

    /// <summary>Temporary flat bonus to defense for DurationTicks ticks — a "temporary resistance" potion (this engine has no elemental damage types to resist individually, so resistance = reduced incoming damage = a defense buff, the same mechanism as an attack/defense-boosting stat potion).</summary>
    BuffDefense,
}
