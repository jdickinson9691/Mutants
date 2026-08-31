namespace ChronTravelers.Core.Time;

/// <summary>
/// One "era band" of the 2000–5000 timeline — the thematic layer over the
/// continuous difficulty curve. An era owns a pool of room-description
/// lines, the monster species that roam its years, and the loot themes
/// its stores and drops draw from. A year belongs to the era with the
/// greatest <see cref="FromYear"/> not exceeding it (see
/// <see cref="EraTable.EraForYear"/>). Loaded from <c>eras.json</c>.
/// </summary>
public sealed record EraDefinition(
    int FromYear,
    string Name,
    IReadOnlyList<string> RoomText,
    IReadOnlyList<string> SpeciesIds,
    IReadOnlyList<string> ItemThemeTags);
