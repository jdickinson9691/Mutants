using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;

namespace ChronoTravelers.PlaytestHarness;

/// <summary>
/// A handful of <see cref="PassiveHook"/>s are flat/continuous stat
/// modifiers rather than a discrete conditional effect (see
/// ChronoTravelers.Core.Diagnostics.PassiveActivationTracker's call sites) —
/// they aren't instrumented, so they'd otherwise print as a permanent
/// zero-activation false negative. Called out separately instead.
/// </summary>
public static class ReportPrinter
{
    private static readonly HashSet<PassiveHook> ContinuousNotObserved =
    [
        PassiveHook.ArmorDefenseBonusPct,
        PassiveHook.FlatSpeedBonus,
        PassiveHook.FlatDefenseBonus,
        PassiveHook.TachyonRegenRateBonusPct,
        PassiveHook.TachyonDrainRateReductionPct,
        PassiveHook.OffClassPenaltyReductionPct,
        PassiveHook.AggroGainReductionPct,
    ];

    public static void Print(CharacterClass characterClass, IReadOnlyList<RunReport> runs)
    {
        Console.WriteLine("========================================================");
        Console.WriteLine($" {characterClass} test battery — {runs.Count} run(s)");
        Console.WriteLine("========================================================");

        for (var i = 0; i < runs.Count; i++)
        {
            PrintRun(i + 1, runs[i]);
        }

        PrintAggregateAbilityUsage(runs);
        PrintAggregatePassiveUsage(characterClass, runs);
    }

    private static void PrintRun(int index, RunReport r)
    {
        Console.WriteLine();
        Console.WriteLine($"--- Run {index} (seed {r.WorldSeed}) ---");
        Console.WriteLine($"  Outcome:        {(r.DiedDuringRun ? $"DIED at tick {r.TicksSurvived}" : "survived to tick budget")}");
        Console.WriteLine($"  Level reached:  {r.FinalLevel}");
        Console.WriteLine($"  Year reached:   {r.FinalYear} (furthest {r.FurthestYearReached})");
        Console.WriteLine($"  Ticks run:      {r.TicksRun}");
        Console.WriteLine($"  Kills:          {r.Kills}");
        Console.WriteLine($"  Total XP:       {r.TotalXp}");
        Console.WriteLine($"  Max hit taken:  {r.MaxHitTaken}");
        Console.WriteLine($"  Ambushes seen:  {r.AmbushesObserved}");
        Console.WriteLine($"  Credits/Tachyons at end: {r.FinalCredits} / {r.FinalTachyons}");

        Console.WriteLine("  Ability usage:");
        if (r.AbilityUsage.Count == 0)
        {
            Console.WriteLine("    (none cast this run)");
        }
        else
        {
            foreach (var (name, usage) in r.AbilityUsage.OrderByDescending(kv => kv.Value.Attempts))
            {
                Console.WriteLine($"    {name,-28} attempts={usage.Attempts,-4} success={usage.Successes,-4} failed={usage.Failures}");
            }
        }

        Console.WriteLine("  Passive activations:");
        if (r.PassiveUsage.Count == 0)
        {
            Console.WriteLine("    (none observed this run)");
        }
        else
        {
            foreach (var (hook, usage) in r.PassiveUsage.OrderByDescending(kv => kv.Value.Activations))
            {
                Console.WriteLine($"    {hook,-28} activations={usage.Activations,-5} totalMagnitude={usage.TotalMagnitude:F1}");
            }
        }

        if (r.UnlockedButUnobserved.Count > 0)
        {
            Console.WriteLine($"  Unlocked but never observed: {string.Join(", ", r.UnlockedButUnobserved)}");
        }
    }

    private static void PrintAggregateAbilityUsage(IReadOnlyList<RunReport> runs)
    {
        Console.WriteLine();
        Console.WriteLine("--- Aggregate ability usage across all runs ---");
        var byName = runs.SelectMany(r => r.AbilityUsage)
            .GroupBy(kv => kv.Key)
            .Select(g => (Name: g.Key, Attempts: g.Sum(kv => kv.Value.Attempts), Successes: g.Sum(kv => kv.Value.Successes), Failures: g.Sum(kv => kv.Value.Failures)))
            .OrderByDescending(x => x.Attempts)
            .ToList();

        if (byName.Count == 0)
        {
            Console.WriteLine("  (no ability was ever cast across any run — check level gating / Tachyon affordability)");
            return;
        }

        foreach (var (name, attempts, successes, failures) in byName)
        {
            Console.WriteLine($"  {name,-28} attempts={attempts,-4} success={successes,-4} failed={failures}");
        }
    }

    private static void PrintAggregatePassiveUsage(CharacterClass characterClass, IReadOnlyList<RunReport> runs)
    {
        Console.WriteLine();
        Console.WriteLine("--- Aggregate passive activations across all runs ---");
        var byHook = runs.SelectMany(r => r.PassiveUsage)
            .GroupBy(kv => kv.Key)
            .Select(g => (Hook: g.Key, Activations: g.Sum(kv => kv.Value.Activations), TotalMagnitude: g.Sum(kv => kv.Value.TotalMagnitude)))
            .OrderByDescending(x => x.Activations)
            .ToList();

        foreach (var (hook, activations, totalMagnitude) in byHook)
        {
            Console.WriteLine($"  {hook,-28} activations={activations,-5} totalMagnitude={totalMagnitude:F1}");
        }

        var everUnlocked = PassiveTraits.All.Where(p => p.Class == characterClass).Select(p => p.Hook).ToHashSet();
        var neverObserved = everUnlocked.Except(byHook.Select(x => x.Hook)).ToList();
        if (neverObserved.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Never observed this class (either unlocked but never triggered, or one of the continuous/unmeasured hooks):");
            foreach (var hook in neverObserved)
            {
                var note = ContinuousNotObserved.Contains(hook) ? " [continuous — not instrumented, always-on once unlocked]" : "";
                Console.WriteLine($"    {hook}{note}");
            }
        }
    }
}
