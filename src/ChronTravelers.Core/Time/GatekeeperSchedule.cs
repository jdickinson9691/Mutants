namespace ChronTravelers.Core.Time;

/// <summary>
/// Where the Gatekeepers stand on the timeline — docs/GDD.md §3.2. From a
/// world seed, walk forward from year 2000 adding a random gap of
/// <see cref="MinGap"/>–<see cref="MaxGap"/> years each step, placing a
/// Gatekeeper at every landing year up to 5000. Deterministic per seed
/// (two <see cref="TimeWorld"/>s built from the same save agree), and
/// different seeds give different schedules. Year 2000 is never a
/// Gatekeeper year — the "present" needs no boss, matching the old
/// level 1.
///
/// Gatekeepers no longer gate travel: a Gatekeeper year just means a
/// tough guaranteed encounter guarding a year-scaled Legendary trophy
/// (see <see cref="TimelineContentFactory.Gatekeeper"/>) until beaten
/// once.
/// </summary>
public sealed class GatekeeperSchedule
{
    public const int MinGap = 50;
    public const int MaxGap = 100;

    private readonly SortedSet<int> _years = [];

    public GatekeeperSchedule(long worldSeed)
    {
        var rng = DeterministicRandom.For(worldSeed, year: 0, purpose: "gatekeeper-schedule");

        var year = TimeScale.MinYear;
        while (true)
        {
            year += rng.Next(MinGap, MaxGap + 1);
            if (year > TimeScale.MaxYear)
            {
                break;
            }

            _years.Add(year);
        }
    }

    /// <summary>Every Gatekeeper year, ascending.</summary>
    public IReadOnlyCollection<int> Years => _years;

    public bool IsGatekeeperYear(int year) => _years.Contains(year);

    /// <summary>Gatekeeper years within [min(a,b), max(a,b)], ascending.</summary>
    public IEnumerable<int> Between(int a, int b) =>
        _years.GetViewBetween(Math.Min(a, b), Math.Max(a, b));

    /// <summary>The nearest Gatekeeper year strictly after <paramref name="year"/>, or null if none remain.</summary>
    public int? NextAfter(int year)
    {
        foreach (var candidate in _years)
        {
            if (candidate > year)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>The nearest Gatekeeper year strictly before <paramref name="year"/>, or null if none.</summary>
    public int? PreviousBefore(int year)
    {
        int? found = null;
        foreach (var candidate in _years)
        {
            if (candidate < year)
            {
                found = candidate;
            }
            else
            {
                break;
            }
        }

        return found;
    }
}
