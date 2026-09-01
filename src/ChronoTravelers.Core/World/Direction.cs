namespace ChronoTravelers.Core.World;

/// <summary>
/// Movement directions. docs/GDD.md §3.1: single-letter commands n/s/e/w
/// for v1; ne/nw/se/sw are an explicit v1.1 stretch goal, not implemented
/// here.
/// </summary>
public enum Direction
{
    North,
    South,
    East,
    West,
}

public static class DirectionExtensions
{
    public static Direction Opposite(this Direction direction) => direction switch
    {
        Direction.North => Direction.South,
        Direction.South => Direction.North,
        Direction.East => Direction.West,
        Direction.West => Direction.East,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };

    /// <summary>Single-letter command per docs/GDD.md §3.1 (e.g. "n").</summary>
    public static string Command(this Direction direction) => direction switch
    {
        Direction.North => "n",
        Direction.South => "s",
        Direction.East => "e",
        Direction.West => "w",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };

    /// <summary>Full lowercase name, as used in exit listings (e.g. "north - area continues.").</summary>
    public static string Name(this Direction direction) => direction switch
    {
        Direction.North => "north",
        Direction.South => "south",
        Direction.East => "east",
        Direction.West => "west",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };

    /// <summary>Parses a single-letter or full-word direction command (case-insensitive). Null if unrecognized.</summary>
    public static Direction? Parse(string input)
    {
        return input.Trim().ToLowerInvariant() switch
        {
            "n" or "north" => Direction.North,
            "s" or "south" => Direction.South,
            "e" or "east" => Direction.East,
            "w" or "west" => Direction.West,
            _ => null,
        };
    }
}
