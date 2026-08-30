namespace Mutants.Engine.Npc;

/// <summary>
/// What an NPC decided to do on a given tick — docs/GDD.md §7's behavior
/// loop ("assess Ion level ... assess HP ... otherwise pursue its current
/// goal"). Store-visiting and time-travel goals from that section aren't
/// modeled yet — they depend on systems that don't exist (stores =
/// milestone 5, time travel = milestone 6).
/// </summary>
public enum NpcGoal
{
    /// <summary>Already defeated; does nothing. No respawn yet (mirrors the player's own deferred death/recall — docs/GDD.md §3.3).</summary>
    Idle,

    /// <summary>Ions are low and inventory fodder was converted for more.</summary>
    SeekIons,

    /// <summary>HP is low; sits out this tick rather than fighting. A placeholder for real heal/flee behavior.</summary>
    Retreat,

    /// <summary>Default goal: wander a step, then fight a monster.</summary>
    Grind,
}
