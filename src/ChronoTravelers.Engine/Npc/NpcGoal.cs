namespace ChronoTravelers.Engine.Npc;

/// <summary>
/// What an NPC decided to do on a given tick — docs/GDD.md §7's behavior
/// loop ("assess Tachyon level ... assess HP ... otherwise pursue its current
/// goal (grind monsters ..., path to a store to trade, attempt a
/// time-travel jump ..., occasionally visit/stock a store it owns)").
/// </summary>
public enum NpcGoal
{
    /// <summary>Already defeated; does nothing. No respawn yet (mirrors the player's own deferred death/recall — docs/GDD.md §3.3).</summary>
    Idle,

    /// <summary>Tachyons are low and inventory fodder was converted for more.</summary>
    SeekTachyons,

    /// <summary>HP is low; sits out this tick rather than fighting. A placeholder for real heal/flee behavior.</summary>
    Retreat,

    /// <summary>Visited a store this tick — sold excess junk or surplus gear, or bought a needed weapon.</summary>
    Trade,

    /// <summary>Swapped to a better weapon/armor/ranged item already sitting in the pack (looted, not bought) — no store needed.</summary>
    Upgrade,

    /// <summary>Attempted a time-travel jump to the next-deeper level this tick — win or lose (see NpcTickResult.Fight for a warden attempt).</summary>
    Travel,

    /// <summary>Default goal: wander a step, then fight a monster.</summary>
    Grind,
}
