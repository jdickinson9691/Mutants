namespace Mutants.Core.Time;

/// <summary>
/// The ordered set of <see cref="EraDefinition"/>s covering the whole
/// timeline. The first era must start exactly at
/// <see cref="TimeScale.MinYear"/> (so every valid year resolves to an
/// era), <see cref="EraDefinition.FromYear"/> values must strictly
/// ascend and stay within the timeline, and every era must carry at
/// least one room-text line and one species id.
/// </summary>
public sealed class EraTable
{
    private readonly List<EraDefinition> _eras;

    public IReadOnlyList<EraDefinition> Eras => _eras;

    public EraTable(IEnumerable<EraDefinition> eras)
    {
        _eras = eras.OrderBy(e => e.FromYear).ToList();

        if (_eras.Count == 0)
        {
            throw new ArgumentException("The timeline needs at least one era.", nameof(eras));
        }

        if (_eras[0].FromYear != TimeScale.MinYear)
        {
            throw new ArgumentException(
                $"The first era must start at year {TimeScale.MinYear} (found {_eras[0].FromYear}).", nameof(eras));
        }

        for (var i = 0; i < _eras.Count; i++)
        {
            var era = _eras[i];

            if (era.FromYear > TimeScale.MaxYear)
            {
                throw new ArgumentException(
                    $"Era '{era.Name}' starts at {era.FromYear}, past the end of the timeline ({TimeScale.MaxYear}).", nameof(eras));
            }

            if (i > 0 && era.FromYear == _eras[i - 1].FromYear)
            {
                throw new ArgumentException(
                    $"Two eras both start at year {era.FromYear} ('{_eras[i - 1].Name}' and '{era.Name}').", nameof(eras));
            }

            if (era.RoomText.Count == 0)
            {
                throw new ArgumentException($"Era '{era.Name}' has no room-text lines.", nameof(eras));
            }

            if (era.SpeciesIds.Count == 0)
            {
                throw new ArgumentException($"Era '{era.Name}' lists no monster species.", nameof(eras));
            }
        }
    }

    /// <summary>The era a given year falls in — the last one whose <see cref="EraDefinition.FromYear"/> is ≤ <paramref name="year"/>.</summary>
    public EraDefinition EraForYear(int year)
    {
        if (!TimeScale.IsValidYear(year))
        {
            throw new ArgumentOutOfRangeException(
                nameof(year), year, $"Year must be between {TimeScale.MinYear} and {TimeScale.MaxYear}.");
        }

        var current = _eras[0];
        foreach (var era in _eras)
        {
            if (era.FromYear <= year)
            {
                current = era;
            }
            else
            {
                break;
            }
        }

        return current;
    }
}
