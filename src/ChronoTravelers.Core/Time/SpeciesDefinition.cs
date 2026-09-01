namespace ChronoTravelers.Core.Time;

/// <summary>
/// One monster "species" in the timeline catalog — a name, some tags, a
/// combat <see cref="MonsterArchetype"/>, and the loot themes it drops.
/// It carries no numbers: <see cref="TimelineContentFactory.ForSpecies"/>
/// scales it to whatever year it's encountered in. Loaded from
/// <c>monster-species.json</c> (see ChronoTravelers.Engine.Content).
/// </summary>
public sealed record SpeciesDefinition(
    string Id,
    string Name,
    IReadOnlyList<string> Tags,
    MonsterArchetype Archetype,
    IReadOnlyList<string> LootThemeTags)
{
    public bool HasTag(string tag) => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
}
