namespace ChronTravelers.Core.World;

/// <summary>
/// A single room on a level's grid. docs/GDD.md §3.1: rooms carry a short
/// atmospheric one-line description, and each exit carries its own short
/// descriptive phrase (e.g. "north - area continues.").
/// </summary>
public sealed record Room(string Description, IReadOnlyDictionary<Direction, string> ExitDescriptions)
{
    public static Room Create(string description, params (Direction Direction, string Text)[] exits) =>
        new(description, exits.ToDictionary(e => e.Direction, e => e.Text));

    public bool HasExit(Direction direction) => ExitDescriptions.ContainsKey(direction);
}
