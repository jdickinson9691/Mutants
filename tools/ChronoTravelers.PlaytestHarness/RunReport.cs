using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Traits;

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

/// <summary>
/// One NPC's final state at the end of a run, tagged by its trait at that
/// point (a respawned NPC rerolls, so this is "whatever it currently is,"
/// not its full-run history) — the raw material for comparing a trait's
/// actual effect against the None baseline rather than just how often it
/// spawns. <c>OwnsStore</c> is true if it holds any store slot across any
/// year the world has visited by run's end. <c>KillCount</c> is scoped the
/// same way as everything else here — kills since this NPC's current
/// incarnation spawned/respawned, via WorldSimulation.OnNpcAct — added
/// specifically to check whether a trait's Credits/Level gap comes from
/// fighting more/less, or from the same fight count paying off
/// differently (see the "PackLeader/Ambusher progress well but earn
/// less" finding this was built to chase down).
/// </summary>
public sealed record NpcOutcome(CreatureTraitKind Trait, int Level, int Credits, int InventoryCount, int FurthestYearReached, bool OwnsStore, int KillCount);

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
    public int MaxHitTaken { get; private set; }
    public int AmbushesObserved { get; set; }

    /// <summary>
    /// Updates <see cref="MaxHitTaken"/> if <paramref name="damage"/> is a
    /// new high — callers report the damage from one concrete blow (one
    /// combat round, one ambush), never a coarser span like "before this
    /// fight vs. after it," which would silently sum every round's damage
    /// into what reads as a single spike (a real bug this once caused:
    /// seven ordinary 4-6 damage hits across a whole fight reported as one
    /// 35-damage hit).
    /// </summary>
    public void RecordHit(int damage)
    {
        if (damage > MaxHitTaken)
        {
            MaxHitTaken = damage;
        }
    }

    public int FinalCredits { get; set; }
    public int FinalTachyons { get; set; }

    public Dictionary<string, AbilityUsage> AbilityUsage { get; } = [];
    public Dictionary<PassiveHook, PassiveUsage> PassiveUsage { get; } = [];

    /// <summary>Passives this character had unlocked by <see cref="FinalLevel"/> that never got a recorded activation this run (either genuinely never triggered, or one of the "continuous, not observed" hooks — see ReportPrinter).</summary>
    public List<string> UnlockedButUnobserved { get; } = [];

    /// <summary>How many fights (regardless of outcome) the bot had against a monster with each <see cref="CreatureTraitKind"/> — recorded by FightBot.Fight. <c>None</c> is the ~60% trait-free baseline every other kind is compared against.</summary>
    public Dictionary<CreatureTraitKind, int> MonsterTraitsFought { get; } = [];

    /// <summary>The trait each NPC in the harness's spawned population carries, sampled at the end of the run (a respawned NPC rerolls, so this reflects current composition, not full-run history).</summary>
    public Dictionary<CreatureTraitKind, int> NpcTraitsObserved { get; } = [];

    /// <summary>Each spawned NPC's final state, for measuring a trait's actual effect (Credits, Level, inventory, store ownership) rather than just its spawn rate.</summary>
    public List<NpcOutcome> NpcOutcomes { get; } = [];
}
