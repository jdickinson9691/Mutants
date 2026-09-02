namespace ChronoTravelers.Core.Items;

/// <summary>
/// What a Consumable item does when used — docs/GDD.md §5's wield/sell/
/// convert three-way choice gets a fourth option for this ItemType:
/// use/eat/drink it. Deliberately a smaller taxonomy than
/// ChronoTravelers.Engine.Combat.AbilityEffectType: everything here is
/// self-directed and usable outside combat too (there's no "target," so
/// no Damage/debuff-style effects), and this type has to live in
/// ChronoTravelers.Core (Item's own namespace) rather than alongside abilities in
/// ChronoTravelers.Engine, since Core doesn't reference Engine.
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

    // --- Permanent stat elixirs -----------------------------------------
    // Each raises one primary stat by Magnitude points *for good* — no
    // duration, it just rewrites the StatBlock the way a level-up does.
    // Rare floor loot (see ChronoTravelers.Core.Time.TimelineContentFactory
    // .StatElixir); DurationTicks is unused for these.

    /// <summary>Permanently adds Magnitude to Strength.</summary>
    BoostStrength,

    /// <summary>Permanently adds Magnitude to Agility.</summary>
    BoostAgility,

    /// <summary>Permanently adds Magnitude to Resolve.</summary>
    BoostResolve,

    /// <summary>Permanently adds Magnitude to Intellect.</summary>
    BoostIntellect,

    /// <summary>
    /// Permanently adds Magnitude to a stat picked at the moment it's
    /// drunk, not baked into the item at spawn time — the ordinary floor
    /// spawn for a Meridian Serum (see
    /// ChronoTravelers.Core.Time.TimelineContentFactory.StatElixir(Random, int)).
    /// A serum pre-rolled to one stat before anyone found it was a dead
    /// item for four of the five classes (only a class's own primary stat,
    /// or Agility, does anything mechanically for it); asking at drink time
    /// instead makes every serum useful to whoever picks it up. The four
    /// Boost&lt;Stat&gt; values above still exist for a serum built with a
    /// stat already fixed (tests, scripted rewards) — see
    /// ChronoTravelers.Core.Characters.Traveler.Consume for how each is
    /// applied.
    /// </summary>
    BoostChosenStat,
}
