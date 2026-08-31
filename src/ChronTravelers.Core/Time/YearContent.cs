using ChronTravelers.Core.Economy;
using ChronTravelers.Core.Monsters;
using ChronTravelers.Core.World;

namespace ChronTravelers.Core.Time;

/// <summary>
/// Everything the game needs about one year — the continuous-timeline
/// replacement for the old <c>WorldLevelDefinition</c>. Produced (and
/// memoized) by <see cref="TimeWorld.GetYear"/>. The map layout is a pure
/// function of the world seed and the year; <see cref="MonsterRoster"/> /
/// <see cref="StoreSlots"/> are rebuilt per year but <see cref="Population"/>
/// is live mutable state (monsters roam and die, loot piles up) that
/// <see cref="TimeWorld"/>'s memo keeps stable for the session.
/// </summary>
public sealed record YearContent(
    int Year,
    LevelMap Map,
    EraDefinition Era,
    IReadOnlyList<Func<Monster>> MonsterRoster,
    IReadOnlyList<StoreSlot> StoreSlots,
    Func<Monster>? Warden,
    double Tier,
    YearPopulation Population)
{
    public bool IsWardenYear => Warden is not null;
}
