namespace ChronoTravelers.Core.Time;

/// <summary>
/// One monster "species" in the timeline catalog — a name, some tags, a
/// combat <see cref="MonsterArchetype"/>, and the loot themes it drops.
/// It carries no numbers: <see cref="TimelineContentFactory.ForSpecies"/>
/// scales it to whatever year it's encountered in. Loaded from
/// <c>monster-generations.json</c> (see ChronoTravelers.Engine.Content).
/// </summary>
public sealed record SpeciesDefinition(
    string Id,
    string Name,
    IReadOnlyList<string> Tags,
    MonsterArchetype Archetype,
    IReadOnlyList<string> LootThemeTags,
    PowerProfile? PowerProfile = null,
    BehaviorProfile? BehaviorProfile = null)
{
    public bool HasTag(string tag) => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);

    /// <summary>This species' stat multipliers, or <see cref="Time.PowerProfile.Default"/> (every multiplier 1.0 — exactly today's archetype-only stats) when it doesn't author one.</summary>
    public PowerProfile EffectivePowerProfile => PowerProfile ?? Time.PowerProfile.Default;

    /// <summary>
    /// This species' behavior traits, or an archetype-based default when
    /// it doesn't author one: a Bruiser stands its ground (never flees);
    /// every other archetype breaks and runs once its HP drops to a
    /// quarter or below and it isn't sharing the player's room (see
    /// <c>ChronoTravelers.Engine.Npc.MonsterController</c>'s ranged-hit
    /// reaction). Every other trait defaults off either way.
    /// </summary>
    public BehaviorProfile EffectiveBehaviorProfile => BehaviorProfile ?? new BehaviorProfile(
        FleeBelowHpFraction: Archetype == MonsterArchetype.Bruiser ? 0.0 : 0.25);
}
