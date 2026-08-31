namespace ChronTravelers.Core.World;

/// <summary>
/// Builds a fully-connected grid of rooms from a coordinate→description
/// map, auto-wiring an exit between every pair of adjacent cells that
/// both have content. Shared by every sandbox level (World.TestLevel,
/// Levels.TestWorld's deeper levels) so the wiring logic lives in one
/// place.
/// </summary>
public static class GridLevelBuilder
{
    public const string DefaultExitFlavor = "area continues.";

    public static LevelMap Build(
        string name,
        Coordinate start,
        IReadOnlyDictionary<Coordinate, string> descriptions,
        string exitFlavor = DefaultExitFlavor)
    {
        var rooms = new Dictionary<Coordinate, Room>();
        foreach (var (coordinate, description) in descriptions)
        {
            var exits = Enum.GetValues<Direction>()
                .Where(direction => descriptions.ContainsKey(coordinate.Move(direction)))
                .Select(direction => (direction, exitFlavor))
                .ToArray();

            rooms[coordinate] = Room.Create(description, exits);
        }

        return new LevelMap(name, start, rooms);
    }
}
