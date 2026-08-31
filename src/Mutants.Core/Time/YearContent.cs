using Mutants.Core.Economy;
using Mutants.Core.Monsters;
using Mutants.Core.World;

namespace Mutants.Core.Time;

/// <summary>
/// Everything the game needs about one year — the continuous-timeline
/// replacement for the old <c>WorldLevelDefinition</c>. Produced (and
/// memoized) by <see cref="TimeWorld.GetYear"/>. The map layout is a pure
/// function of the world seed and the year; the roster and store slots
/// are rebuilt on demand, so wandering monsters and loot are fresh each
/// visit while <see cref="TimeWorld"/>'s memo keeps a revisit stable
/// within a session.
/// </summary>
public sealed record YearContent(
    int Year,
    LevelMap Map,
    EraDefinition Era,
    IReadOnlyList<Func<Monster>> MonsterRoster,
    IReadOnlyList<StoreSlot> StoreSlots,
    Func<Monster>? Gatekeeper,
    double Tier)
{
    public bool IsGatekeeperYear => Gatekeeper is not null;
}
