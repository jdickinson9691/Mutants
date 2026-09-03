namespace ChronoTravelers.Core.Time;

/// <summary>
/// The ordered set of <see cref="GenerationDefinition"/>s covering the
/// whole timeline — the monster-roster counterpart to <see cref="EraTable"/>,
/// same shape and same validation: the first generation must start
/// exactly at <see cref="TimeScale.MinYear"/> (so every valid year
/// resolves to one), <see cref="GenerationDefinition.FromYear"/> values
/// must strictly ascend and stay within the timeline, and every
/// generation must carry at least one species. Species ids must also be
/// unique across every generation put together, since
/// <see cref="TimeWorld"/> treats the whole table as one flat catalog.
/// </summary>
public sealed class GenerationTable
{
    private readonly List<GenerationDefinition> _generations;

    public IReadOnlyList<GenerationDefinition> Generations => _generations;

    public GenerationTable(IEnumerable<GenerationDefinition> generations)
    {
        _generations = generations.OrderBy(g => g.FromYear).ToList();

        if (_generations.Count == 0)
        {
            throw new ArgumentException("The timeline needs at least one monster generation.", nameof(generations));
        }

        if (_generations[0].FromYear != TimeScale.MinYear)
        {
            throw new ArgumentException(
                $"The first monster generation must start at year {TimeScale.MinYear} (found {_generations[0].FromYear}).", nameof(generations));
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _generations.Count; i++)
        {
            var generation = _generations[i];

            if (generation.FromYear > TimeScale.MaxYear)
            {
                throw new ArgumentException(
                    $"Monster generation '{generation.Name}' starts at {generation.FromYear}, past the end of the timeline ({TimeScale.MaxYear}).", nameof(generations));
            }

            if (i > 0 && generation.FromYear == _generations[i - 1].FromYear)
            {
                throw new ArgumentException(
                    $"Two monster generations both start at year {generation.FromYear} ('{_generations[i - 1].Name}' and '{generation.Name}').", nameof(generations));
            }

            if (generation.Species.Count == 0)
            {
                throw new ArgumentException($"Monster generation '{generation.Name}' lists no species.", nameof(generations));
            }

            foreach (var species in generation.Species)
            {
                if (!seenIds.Add(species.Id))
                {
                    throw new ArgumentException($"Duplicate species id '{species.Id}' (monster generation '{generation.Name}').", nameof(generations));
                }
            }
        }
    }

    /// <summary>The monster generation a given year falls in — the last one whose <see cref="GenerationDefinition.FromYear"/> is ≤ <paramref name="year"/>.</summary>
    public GenerationDefinition GenerationForYear(int year)
    {
        if (!TimeScale.IsValidYear(year))
        {
            throw new ArgumentOutOfRangeException(
                nameof(year), year, $"Year must be between {TimeScale.MinYear} and {TimeScale.MaxYear}.");
        }

        var current = _generations[0];
        foreach (var generation in _generations)
        {
            if (generation.FromYear <= year)
            {
                current = generation;
            }
            else
            {
                break;
            }
        }

        return current;
    }
}
