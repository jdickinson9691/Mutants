namespace ChronoTravelers.Core.Time;

/// <summary>
/// One "era band" of the 2000–5000 timeline — the thematic layer over the
/// continuous difficulty curve. An era owns a pool of room-description
/// lines and the loot themes its stores and drops draw from. (Which
/// monster species roam a year is a separate, independent axis — see
/// <see cref="GenerationDefinition"/> — on its own, longer, fixed 500-year
/// cadence; the two bandings deliberately don't line up.) A year belongs
/// to the era with the greatest <see cref="FromYear"/> not exceeding it
/// (see <see cref="EraTable.EraForYear"/>). Loaded from <c>eras.json</c>.
/// </summary>
public sealed record EraDefinition(
    int FromYear,
    string Name,
    IReadOnlyList<string> RoomText,
    IReadOnlyList<string> ItemThemeTags);
