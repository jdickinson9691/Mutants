using Mutants.Core.Characters;
using Mutants.Core.Items;
using Mutants.Core.Monsters;
using Mutants.Core.World;
using Mutants.Engine.Combat;

namespace Mutants.Engine.Npc;

/// <summary>
/// The per-tick NPC behavior loop — docs/GDD.md §7: "assess Ion level
/// (seek conversion fodder or a store if low), assess HP (retreat/heal if
/// low), otherwise pursue its current goal (grind monsters near its
/// level...)." The GDD also lists store-visiting and time-travel goals;
/// those are deferred (see <see cref="NpcGoal"/>) since stores/time
/// travel don't exist yet. Decision-making is intentionally simple and
/// rule-based per the GDD's own "not full pathfinding AI/ML" note; the
/// specific Ion/HP thresholds below are original tuning, not
/// GDD-specified.
/// </summary>
public static class NpcController
{
    private const double LowIonThreshold = 0.25;
    private const double LowHealthThreshold = 0.30;

    /// <summary>
    /// Decides and executes one tick's action for <paramref name="npc"/>.
    /// A defeated NPC does nothing (<see cref="NpcGoal.Idle"/>) — callers
    /// should generally skip calling this for dead NPCs entirely, but it's
    /// safe to call anyway.
    /// </summary>
    public static NpcTickResult Act(Mutant npc, LevelMap level, IRandomSource random)
    {
        if (npc.Health.IsDead)
        {
            return new NpcTickResult(npc.Name, NpcGoal.Idle);
        }

        if (IsLow(npc.Ions.Current, npc.Ions.Max, LowIonThreshold))
        {
            var convertible = npc.Inventory.FirstOrDefault(i => i.Type == ItemType.Junk)
                               ?? npc.Inventory.FirstOrDefault();
            if (convertible is not null)
            {
                npc.Convert(convertible);
                return new NpcTickResult(npc.Name, NpcGoal.SeekIons);
            }
            // No fodder on hand - fall through to grinding, which is also how they'll find more.
        }

        if (IsLow(npc.Health.Current, npc.Health.Max, LowHealthThreshold))
        {
            return new NpcTickResult(npc.Name, NpcGoal.Retreat);
        }

        Wander(npc, level, random);

        var monster = TestMonsters.All[(int)(random.NextDouble() * TestMonsters.All.Count)]();
        var fight = CombatResolver.Fight(npc, monster, random);

        return new NpcTickResult(npc.Name, NpcGoal.Grind, monster.Name, fight);
    }

    private static bool IsLow(int current, int max, double threshold) => max > 0 && current < max * threshold;

    private static void Wander(Mutant npc, LevelMap level, IRandomSource random)
    {
        var room = level.GetRoom(npc.Position);
        var exits = room.ExitDescriptions.Keys.ToList();
        if (exits.Count == 0)
        {
            return;
        }

        var direction = exits[(int)(random.NextDouble() * exits.Count)];
        var move = level.TryMove(npc.Position, direction);
        if (move.Success)
        {
            npc.MoveTo(move.Destination!.Value);
        }
    }
}
