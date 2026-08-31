using ChronTravelers.Core.Classes;

namespace ChronTravelers.Core.Stats;

/// <summary>
/// The four primary stats a character carries. Immutable — leveling up
/// produces a new StatBlock rather than mutating one in place, so stat
/// history/replay stays simple.
/// </summary>
public readonly record struct StatBlock(int Strength, int Agility, int Resolve, int Intellect)
{
    public int Get(PrimaryStat stat) => stat switch
    {
        PrimaryStat.Strength => Strength,
        PrimaryStat.Agility => Agility,
        PrimaryStat.Resolve => Resolve,
        PrimaryStat.Intellect => Intellect,
        _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, null),
    };

    /// <summary>Returns a copy with <paramref name="amount"/> added to a single stat.</summary>
    public StatBlock Increase(PrimaryStat stat, int amount = 1) => stat switch
    {
        PrimaryStat.Strength => this with { Strength = Strength + amount },
        PrimaryStat.Agility => this with { Agility = Agility + amount },
        PrimaryStat.Resolve => this with { Resolve = Resolve + amount },
        PrimaryStat.Intellect => this with { Intellect = Intellect + amount },
        _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, null),
    };
}
