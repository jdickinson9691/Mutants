namespace Mutants.Core.World;

/// <summary>
/// A room's position on a level's grid, addressed by East/West and
/// North/South offsets from the level's origin — matches the surviving
/// screenshot's "Compass: (2E : 0N)" readout, per docs/GDD.md §3.1.
/// East and North are signed: negative East is west of origin, negative
/// North is south of origin.
/// </summary>
public readonly record struct Coordinate(int East, int North)
{
    public static readonly Coordinate Origin = new(0, 0);

    public Coordinate Move(Direction direction) => direction switch
    {
        Direction.North => this with { North = North + 1 },
        Direction.South => this with { North = North - 1 },
        Direction.East => this with { East = East + 1 },
        Direction.West => this with { East = East - 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };

    /// <summary>Renders like the source game's compass readout, e.g. "(2E : 0N)".</summary>
    public override string ToString() => $"({East}E : {North}N)";
}
