using ChronTravelers.Core.World;

namespace ChronTravelers.Core.Time;

/// <summary>
/// Generates a year's room grid deterministically from the world seed and
/// the year — the same year always produces the same map, so it can be a
/// pure function of the save (nothing about the layout is stored). Room
/// descriptions are drawn from the year's <see cref="EraDefinition.RoomText"/>
/// pool. Exit wiring is handled by the existing
/// <see cref="GridLevelBuilder"/> (every adjacent pair of occupied cells
/// gets a two-way exit), so the grid is always fully connected.
/// </summary>
public static class YearMapFactory
{
    private const int MinRooms = 9;
    private const int MaxRooms = 25;

    public static LevelMap Build(long worldSeed, EraDefinition era, int year)
    {
        var rng = DeterministicRandom.For(worldSeed, year, "map");
        var targetRooms = rng.Next(MinRooms, MaxRooms + 1);

        // Grow a connected blob outward from the origin: repeatedly pick an
        // already-placed cell and step one cardinal direction into a new
        // cell. Guaranteed connected; organic-looking rather than a full
        // rectangle.
        var placed = new List<Coordinate> { Coordinate.Origin };
        var occupied = new HashSet<Coordinate> { Coordinate.Origin };
        var directions = Enum.GetValues<Direction>();

        var guard = 0;
        while (occupied.Count < targetRooms && guard++ < targetRooms * 50)
        {
            var from = placed[rng.Next(placed.Count)];
            var next = from.Move(directions[rng.Next(directions.Length)]);
            if (occupied.Add(next))
            {
                placed.Add(next);
            }
        }

        var descriptions = new Dictionary<Coordinate, string>(occupied.Count);
        foreach (var coordinate in placed)
        {
            descriptions[coordinate] = era.RoomText[rng.Next(era.RoomText.Count)];
        }

        return GridLevelBuilder.Build($"{era.Name} — {year} A.D.", Coordinate.Origin, descriptions);
    }
}
