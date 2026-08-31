namespace Mutants.Core.Time;

/// <summary>
/// The combat shape a monster species occupies, relative to
/// <see cref="Monsters.MonsterScaling"/>'s baseline for a given year.
/// Content authors pick an archetype per species instead of writing raw
/// stat numbers; <see cref="TimelineContentFactory"/> turns it into
/// concrete stats at the encounter year.
/// </summary>
public enum MonsterArchetype
{
    /// <summary>Exactly the year's baseline HP/attack/defense/speed.</summary>
    Baseline,

    /// <summary>Glass cannon — lower HP/defense, higher attack and speed. Most <c>undead</c> species are casters.</summary>
    Caster,

    /// <summary>Tank — higher HP/defense, lower attack and speed.</summary>
    Bruiser,

    /// <summary>Fast harasser — slightly above baseline HP/attack, well above baseline speed.</summary>
    Skirmisher,
}
