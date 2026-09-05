using ChronoTravelers.Core.World;

namespace ChronoTravelers.Engine.Combat;

/// <summary>
/// The spatial half of <c>point</c>/<c>shoot &lt;dir&gt;</c> — walks a
/// straight, unbroken chain of room exits from a shooter's position in one
/// direction, up to a weapon's <see cref="Core.Items.Item.Range"/>, looking
/// for a specific target's room. Everything else about firing (ammo,
/// damage, aggro) lives in <see cref="RangedResolver"/>; this piece was
/// extracted out of ChronoTravelers.Console's <c>HandleShoot</c> (which
/// still owns command parsing and messaging) so the line-of-sight rule is
/// unit-testable without a live console session — previously it was
/// impossible to exercise without literally typing commands, and had zero
/// test coverage as a result.
/// </summary>
public static class RangedTargeting
{
    /// <summary>
    /// True if <paramref name="targetPosition"/> lies exactly along the
    /// straight corridor of exits from <paramref name="shooterPosition"/>
    /// in <paramref name="direction"/>, within <paramref name="range"/>
    /// rooms (1-based: range 1 only reaches the very next room). False if
    /// the corridor breaks — a missing exit, or an exit into an
    /// unregistered room — before reaching it, or the target isn't out
    /// that way at all within range.
    /// </summary>
    public static bool HasClearShot(LevelMap map, Coordinate shooterPosition, Direction direction, int range, Coordinate targetPosition)
    {
        var current = shooterPosition;
        for (var step = 0; step < range; step++)
        {
            var move = map.TryMove(current, direction);
            if (!move.Success)
            {
                return false; // the corridor doesn't reach any farther that way
            }

            current = move.Destination!.Value;
            if (targetPosition.Equals(current))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The first direction (in <see cref="Direction"/>'s declaration order)
    /// with a clear shot at <paramref name="targetPosition"/>, or null if
    /// none of the four does — for an automated shooter (NPC/bot) that has
    /// to pick a direction itself rather than a player typing one.
    /// </summary>
    public static Direction? FindClearShotDirection(LevelMap map, Coordinate shooterPosition, int range, Coordinate targetPosition)
    {
        foreach (var direction in Enum.GetValues<Direction>())
        {
            if (HasClearShot(map, shooterPosition, direction, range, targetPosition))
            {
                return direction;
            }
        }

        return null;
    }
}
