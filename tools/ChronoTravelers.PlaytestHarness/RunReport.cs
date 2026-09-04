using ChronoTravelers.Core.Characters;

namespace ChronoTravelers.PlaytestHarness;

/// <summary>Cast attempts against one named ability, for one run.</summary>
public sealed class AbilityUsage
{
    public int Attempts { get; set; }
    public int Successes { get; set; }
    public int Failures { get; set; }
}

/// <summary>
/// Observed activations of one <see cref="PassiveHook"/>, for one run — see
/// ChronoTravelers.Core.Diagnostics.PassiveActivationTracker. <c>TotalMagnitude</c>
/// is in whatever concrete unit that hook records (HP, Credits, Tachyons,
/// attack points — a raw count of 1.0 per activation for a hook with no
/// natural unit, e.g. a dodge roll).
/// </summary>
public sealed class PassiveUsage
{
    public int Activations { get; set; }
    public double TotalMagnitude { get; set; }
}

/// <summary>Everything the harness recorded for one bot playthrough of one class.</summary>
public sealed class RunReport
{
    public required string CharacterName { get; init; }
    public required long WorldSeed { get; init; }

    public int FinalLevel { get; set; }
    public int FinalYear { get; set; }
    public int FurthestYearReached { get; set; }
    public bool DiedDuringRun { get; set; }
    public int TicksRun { get; set; }
    public int TicksSurvived { get; set; }

    public int Kills { get; set; }
    public int TotalXp { get; set; }
    public int MaxHitTaken { get; set; }
    public int AmbushesObserved { get; set; }

    public int FinalCredits { get; set; }
    public int FinalTachyons { get; set; }

    public Dictionary<string, AbilityUsage> AbilityUsage { get; } = [];
    public Dictionary<PassiveHook, PassiveUsage> PassiveUsage { get; } = [];

    /// <summary>Passives this character had unlocked by <see cref="FinalLevel"/> that never got a recorded activation this run (either genuinely never triggered, or one of the "continuous, not observed" hooks — see ReportPrinter).</summary>
    public List<string> UnlockedButUnobserved { get; } = [];
}
