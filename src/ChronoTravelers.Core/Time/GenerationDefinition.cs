namespace ChronoTravelers.Core.Time;

/// <summary>
/// One 500-year "monster generation" — the axis that decides which
/// monster species roam a given year. Deliberately independent of
/// <see cref="EraDefinition"/>, which owns room text and item/loot
/// theming on its own, shorter (~200–250 year) cadence: eras are the
/// timeline's flavor/loot layer, generations are its monster-roster
/// layer, and the two bandings don't line up. A year belongs to the
/// generation with the greatest <see cref="FromYear"/> not exceeding it
/// (see <see cref="GenerationTable.GenerationForYear"/>). Loaded from
/// <c>monster-generations.json</c>.
/// </summary>
public sealed record GenerationDefinition(
    int FromYear,
    string Name,
    IReadOnlyList<SpeciesDefinition> Species);
