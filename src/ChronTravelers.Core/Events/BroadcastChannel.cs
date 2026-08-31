namespace ChronTravelers.Core.Events;

/// <summary>
/// In-memory feed of <see cref="GameEvent"/>s — docs/GDD.md §7's shared
/// broadcast channel. No persistence yet (that's future save/leaderboard
/// work, milestone 7); bounded to the most recent <see cref="Capacity"/>
/// events so a long session can't grow it unbounded.
/// </summary>
public sealed class BroadcastChannel(int capacity = 200)
{
    public int Capacity { get; } = capacity > 0
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");

    private readonly List<GameEvent> _events = [];

    public IReadOnlyList<GameEvent> Events => _events;

    public void Publish(GameEvent gameEvent)
    {
        _events.Add(gameEvent);
        if (_events.Count > Capacity)
        {
            _events.RemoveAt(0);
        }
    }

    /// <summary>The most recent events, oldest first, up to <paramref name="count"/>.</summary>
    public IReadOnlyList<GameEvent> Recent(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count cannot be negative.");
        }

        return _events.Skip(Math.Max(0, _events.Count - count)).ToList();
    }
}
