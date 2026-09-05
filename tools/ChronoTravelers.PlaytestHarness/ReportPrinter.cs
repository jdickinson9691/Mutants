using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Traits;

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
        PrintAggregateTraitCounts(runs);
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

        PrintTraitCounts("  Monster traits fought:", r.MonsterTraitsFought);
        PrintTraitCounts("  NPC population traits:", r.NpcTraitsObserved);
    }

    private static void PrintTraitCounts(string label, IReadOnlyDictionary<CreatureTraitKind, int> counts)
    {
        Console.WriteLine(label);
        if (counts.Count == 0)
        {
            Console.WriteLine("    (none)");
            return;
        }

        var total = counts.Values.Sum();
        foreach (var (kind, count) in counts.OrderByDescending(kv => kv.Value))
        {
            Console.WriteLine($"    {kind,-12} {count,-4} ({100.0 * count / total:F0}%)");
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

    private static void PrintAggregateTraitCounts(IReadOnlyList<RunReport> runs)
    {
        Console.WriteLine();
        Console.WriteLine("--- Aggregate creature traits across all runs ---");

        var monsterTotals = new Dictionary<CreatureTraitKind, int>();
        var npcTotals = new Dictionary<CreatureTraitKind, int>();
        foreach (var r in runs)
        {
            foreach (var (kind, count) in r.MonsterTraitsFought)
            {
                monsterTotals[kind] = monsterTotals.GetValueOrDefault(kind) + count;
            }

            foreach (var (kind, count) in r.NpcTraitsObserved)
            {
                npcTotals[kind] = npcTotals.GetValueOrDefault(kind) + count;
            }
        }

        PrintTraitCounts("Monsters fought, by trait (expect ~60% None, ~5% each other kind):", monsterTotals);
        PrintTraitCounts("NPC population, by trait (sampled once per run, so small samples are noisy):", npcTotals);
    }

    /// <summary>
    /// Compares each trait's actual final-state averages (Level, Credits,
    /// inventory size, furthest year, store ownership) against the None
    /// baseline — distribution alone (<see cref="PrintAggregateTraitCounts"/>)
    /// only shows a trait spawns at the right rate, not that it does
    /// anything. Combines NpcOutcomes across every class's battery (the
    /// NPC population excludes whichever class is under test in each one,
    /// so pooling all of them is the only way to get a real sample per
    /// trait instead of ~3 NPCs).
    /// </summary>
    public static void PrintNpcTraitEffects(IReadOnlyList<RunReport> allReportsAcrossEveryClass)
    {
        var outcomes = allReportsAcrossEveryClass.SelectMany(r => r.NpcOutcomes).ToList();

        Console.WriteLine();
        Console.WriteLine("========================================================");
        Console.WriteLine(" NPC trait effects (pooled across every class's battery)");
        Console.WriteLine("========================================================");

        if (outcomes.Count == 0)
        {
            Console.WriteLine("  (no NPC outcomes recorded)");
            return;
        }

        var baseline = outcomes.Where(o => o.Trait == CreatureTraitKind.None).ToList();
        if (baseline.Count == 0)
        {
            Console.WriteLine("  (no None-trait NPCs to use as a baseline)");
            return;
        }

        var baseLevel = baseline.Average(o => o.Level);
        var baseCredits = baseline.Average(o => o.Credits);
        var baseInventory = baseline.Average(o => o.InventoryCount);
        var baseFurthestYear = baseline.Average(o => o.FurthestYearReached);
        var baseStorePct = 100.0 * baseline.Count(o => o.OwnsStore) / baseline.Count;

        Console.WriteLine($"  {"Trait",-12} {"n",-5} {"AvgLevel",-10} {"AvgCredits",-12} {"AvgInv",-8} {"AvgFurthestYr",-14} {"OwnsStore%"}");
        Console.WriteLine($"  {"None",-12} {baseline.Count,-5} {baseLevel,-10:F1} {baseCredits,-12:F0} {baseInventory,-8:F1} {baseFurthestYear,-14:F0} {baseStorePct:F0}%  (baseline)");

        foreach (var kind in Enum.GetValues<CreatureTraitKind>())
        {
            if (kind == CreatureTraitKind.None)
            {
                continue;
            }

            var group = outcomes.Where(o => o.Trait == kind).ToList();
            if (group.Count == 0)
            {
                Console.WriteLine($"  {kind,-12} 0     (no NPCs with this trait spawned)");
                continue;
            }

            var level = group.Average(o => o.Level);
            var credits = group.Average(o => o.Credits);
            var inventory = group.Average(o => o.InventoryCount);
            var furthestYear = group.Average(o => o.FurthestYearReached);
            var storePct = 100.0 * group.Count(o => o.OwnsStore) / group.Count;

            Console.WriteLine($"  {kind,-12} {group.Count,-5} {level,-10:F1} {credits,-12:F0} {inventory,-8:F1} {furthestYear,-14:F0} {storePct:F0}%" +
                $"  (Credits {PercentDelta(credits, baseCredits)}, Level {PercentDelta(level, baseLevel)}, Inv {PercentDelta(inventory, baseInventory)})");
        }

        Console.WriteLine();
        Console.WriteLine("  Expected directions: Hoarder — higher inventory, lower Credits (never sells).");
        Console.WriteLine("  Scavenger — higher Credits (25% sell/convert bonus). Trader — more likely to");
        Console.WriteLine("  own a store. PackLeader/Ambusher — combat-power bonuses, so higher Level/");
        Console.WriteLine("  furthest year if anything. Aggressive/Skittish/Wanderer have no direct stat");
        Console.WriteLine("  hook here — their effect is in fight/retreat/travel frequency, not final state.");
    }

    private static string PercentDelta(double value, double baseline)
    {
        if (baseline == 0)
        {
            return value == 0 ? "n/a" : "n/a (baseline is 0)";
        }

        var pct = 100.0 * (value - baseline) / baseline;
        return $"{(pct >= 0 ? "+" : "")}{pct:F0}% vs None";
    }
}
