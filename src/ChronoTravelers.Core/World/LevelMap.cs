namespace ChronoTravelers.Core.World;

/// <summary>
/// A single time-travel level's grid of rooms — docs/GDD.md §3.1/§3.2.
/// Multi-level content (level themes, monster/loot/store population per
/// level) is future content work; this is just the grid/movement shape.
/// </summary>
public sealed class LevelMap
{
    public string Name { get; }
    public Coordinate Start { get; }

    private readonly Dictionary<Coordinate, Room> _rooms;

    public LevelMap(string name, Coordinate start, IReadOnlyDictionary<Coordinate, Room> rooms)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Level name cannot be empty.", nameof(name));
        }

        if (rooms.Count == 0)
        {
            throw new ArgumentException("A level must contain at least one room.", nameof(rooms));
        }

        if (!rooms.ContainsKey(start))
        {
            throw new ArgumentException($"Start coordinate {start} has no room.", nameof(start));
        }

        Name = name;
        Start = start;
        _rooms = new Dictionary<Coordinate, Room>(rooms);
    }

    /// <summary>Every room on the grid, keyed by coordinate. Read-only.</summary>
    public IReadOnlyDictionary<Coordinate, Room> Rooms => _rooms;

    /// <summary>Number of rooms on the grid.</summary>
    public int RoomCount => _rooms.Count;

    public Room? TryGetRoom(Coordinate coordinate) => _rooms.GetValueOrDefault(coordinate);

    public Room GetRoom(Coordinate coordinate) =>
        TryGetRoom(coordinate) ?? throw new KeyNotFoundException($"No room at {coordinate}.");

    /// <summary>
    /// Attempts to move from <paramref name="from"/> in <paramref name="direction"/>.
    /// Fails with <see cref="MoveFailureReason.NoExit"/> if the room doesn't
    /// list that exit, or <see cref="MoveFailureReason.NoRoomBeyondExit"/> if
    /// it does but no room is registered there (an authoring bug — see
    /// <see cref="Validate"/>).
    /// </summary>
    public MoveResult TryMove(Coordinate from, Direction direction)
    {
        var currentRoom = GetRoom(from);
        if (!currentRoom.HasExit(direction))
        {
            return MoveResult.Blocked(MoveFailureReason.NoExit);
        }

        var destination = from.Move(direction);
        var destinationRoom = TryGetRoom(destination);
        return destinationRoom is null
            ? MoveResult.Blocked(MoveFailureReason.NoRoomBeyondExit)
            : MoveResult.Moved(destination, destinationRoom);
    }

    /// <summary>
    /// Every room reachable from <paramref name="origin"/> within
    /// <paramref name="maxHops"/> exits (breadth-first over each room's
    /// declared exits, same connectivity <see cref="TryMove"/> walks one
    /// step at a time) — <paramref name="origin"/> itself excluded. Backs
    /// Engineer's "Jump Rig" ability (a short teleport rather than a normal
    /// walk — see ChronoTravelers.Engine.Combat.OverworldAbilityResolver).
    /// Empty if <paramref name="origin"/> isn't a room on this map, or if
    /// every room within range has already been visited (impossible in
    /// practice for <paramref name="maxHops"/> ≥ 1 on any level with more
    /// than one room, since <see cref="Validate"/> guarantees every
    /// declared exit leads to a real room).
    /// </summary>
    public IReadOnlyList<Coordinate> RoomsWithinHops(Coordinate origin, int maxHops)
    {
        if (!_rooms.ContainsKey(origin))
        {
            return [];
        }

        var distanceFromOrigin = new Dictionary<Coordinate, int> { [origin] = 0 };
        var queue = new Queue<Coordinate>();
        queue.Enqueue(origin);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var distance = distanceFromOrigin[current];
            if (distance >= maxHops)
            {
                continue;
            }

            foreach (var direction in GetRoom(current).ExitDescriptions.Keys)
            {
                var neighbor = current.Move(direction);
                if (_rooms.ContainsKey(neighbor) && !distanceFromOrigin.ContainsKey(neighbor))
                {
                    distanceFromOrigin[neighbor] = distance + 1;
                    queue.Enqueue(neighbor);
                }
            }
        }

        distanceFromOrigin.Remove(origin);
        return distanceFromOrigin.Keys.ToList();
    }

    /// <summary>
    /// Validates level authoring: every exit a room declares must lead to a
    /// registered room. Returns a human-readable problem for each broken
    /// exit found (empty if the level is well-formed).
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        foreach (var (coordinate, room) in _rooms)
        {
            foreach (var direction in room.ExitDescriptions.Keys)
            {
                var neighbor = coordinate.Move(direction);
                if (!_rooms.ContainsKey(neighbor))
                {
                    problems.Add(
                        $"Room {coordinate} declares an exit {direction.Name()} to {neighbor}, but no room exists there.");
                }
            }
        }

        return problems;
    }
}
