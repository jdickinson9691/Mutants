namespace ChronTravelers.Core.Monsters;

/// <summary>How a monster currently regards the player — derived from its accumulated <see cref="Monster.Aggro"/>.</summary>
public enum AggroMood
{
    /// <summary>Indifferent — wanders normally, doesn't track the player at all.</summary>
    Calm,

    /// <summary>Alerted — shadows the player (moves to close the distance) but won't take a free swing.</summary>
    Alert,

    /// <summary>Hostile — shadows the player and lands an ambush hit on an idle turn.</summary>
    Hostile,
}

/// <summary>
/// Tuning for the earned-aggro system (<see cref="Monster.Aggro"/>).
/// Monsters do <em>not</em> automatically pursue or attack anyone who
/// walks past — a monster only starts hunting the player once its aggro
/// has been raised, chiefly by the player <b>repeatedly entering and
/// leaving its tile</b> (each entry is a solid bump; the effect stacks if
/// you do it faster than aggro decays). Standing on or next to a monster
/// raises it slowly; being shot raises it a lot; moving a couple of rooms
/// away lets it fall back toward indifference. Original tuning — not
/// GDD-specified. See ChronTravelers.Engine.Npc.MonsterController for how the
/// bands drive behaviour.
/// </summary>
public static class AggroModel
{
    /// <summary>Aggro added when the player steps onto a monster's tile from elsewhere — the primary "you keep bugging me" signal.</summary>
    public const double EnterTileAggro = 2.0;

    /// <summary>Aggro added per tick the player spends parked on the monster's tile.</summary>
    public const double CoLocatedPerTick = 0.6;

    /// <summary>Aggro added per tick the player spends in a room adjacent to the monster.</summary>
    public const double AdjacentPerTick = 0.3;

    /// <summary>Aggro added when a ranged shot hits the monster without killing it — it noticed.</summary>
    public const double RangedHitAggro = 4.0;

    /// <summary>Aggro shed per tick while the player is out of aggro range (or safe in a store) — faster than it accrues, so leaving reliably calms a monster.</summary>
    public const double DecayPerTick = 1.5;

    /// <summary>Aggro is clamped to this ceiling.</summary>
    public const double Cap = 12.0;

    /// <summary>At/above this the monster shadows the player.</summary>
    public const double AlertThreshold = 3.0;

    /// <summary>At/above this the monster will ambush an idle player.</summary>
    public const double HostileThreshold = 6.0;

    public static AggroMood MoodFor(double aggro) => aggro switch
    {
        >= HostileThreshold => AggroMood.Hostile,
        >= AlertThreshold => AggroMood.Alert,
        _ => AggroMood.Calm,
    };
}
