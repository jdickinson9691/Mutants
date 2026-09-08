using System.Diagnostics;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Engine.Content;
using ChronoTravelers.PlaytestHarness;

// Per-class test battery for tuning: plays one class through several bot
// runs (spatial movement, real combat via CombatSession, ability casts,
// looting, shopping, time travel — see PlaytestRunner) and reports the
// same survival/progression numbers past ad hoc cold-start playtests
// tracked (level/year reached, kills, deaths, max hit taken), plus real
// observed active-ability cast counts and passive-trait activation counts
// (see ChronoTravelers.Core.Diagnostics.PassiveActivationTracker).
//
// Usage: dotnet run --project tools/ChronoTravelers.PlaytestHarness -- <Class|all> [runs] [ticksPerRun] [seed] [aggression] [verboseFatal]
//   Class:       Soldier | Doctor | Spy | Scientist | Engineer | all
//   runs:        bot playthroughs per class (default 3)
//   ticksPerRun: world-tick budget per run (default 3000)
//   seed:        base world seed; run N uses seed+N (default 1000). For
//                "all", each class also gets its own +100000-per-class
//                offset so classes don't replay the exact same seeds
//                (see the SeedStridePerClass comment below for why that
//                mattered for pooled NPC trait sampling).
//   aggression:  healing-threshold multiplier, >1 = heals later/less
//                cautiously (default 1.0) — see PlaytestRunner.Run's doc
//                comment; useful for surfacing low-HP passives (Second
//                Wind, Unbreakable) the default caution rarely triggers.
//   verboseFatal: 1/true dumps the killing monster's stats + full combat
//                log to stderr for whichever fight kills the bot in each
//                run — useful for tracing a suspicious death (default off).

if (args.Length < 1)
{
    PrintUsage();
    return 1;
}

var classArg = args[0];
// Tolerant parses: "battery" reinterprets these positionals (arg[1] is
// minutes, a double) and handles them in its own branch below, so a
// non-int arg[1] must not blow up here before we get there.
var runs = args.Length > 1 && int.TryParse(args[1], out var runsArg) ? runsArg : 3;
var maxTicks = args.Length > 2 && int.TryParse(args[2], out var maxTicksArg) ? maxTicksArg : 3000;
var baseSeed = args.Length > 3 && long.TryParse(args[3], out var baseSeedArg) ? baseSeedArg : 1000;
var aggression = args.Length > 4 && double.TryParse(args[4], out var aggressionArg) ? aggressionArg : 1.0;
var verboseFatal = args.Length > 5 && args[5] is "1" or "true";

var contentDirectory = Path.Combine(AppContext.BaseDirectory, "Content");
var abilities = LoadAbilities(contentDirectory);

// "simul" / "together": play EVERY class as its own bot in ONE shared
// world (WorldSimulation.TickMultiplayer), running each `runs` times on
// successive seeds, until the last bot in that world dies. `runs` here is
// how many shared-world sessions to play, not per-class playthroughs.
if (string.Equals(classArg, "simul", StringComparison.OrdinalIgnoreCase)
    || string.Equals(classArg, "together", StringComparison.OrdinalIgnoreCase))
{
    var simulClasses = Enum.GetValues<CharacterClass>().ToList();
    for (var session = 0; session < runs; session++)
    {
        var seed = baseSeed + session;
        Console.WriteLine("########################################################");
        Console.WriteLine($" Shared-world session {session + 1}/{runs} (seed {seed}) — all {simulClasses.Count} classes played simultaneously");
        Console.WriteLine("########################################################");
        Console.WriteLine();

        var result = PlaytestRunner.RunSimultaneous(simulClasses, seed, maxTicks, contentDirectory, abilities, aggression, verboseFatal);

        foreach (var report in result.PerClass)
        {
            ReportPrinter.PrintSimultaneousClassReport(report);
        }

        ReportPrinter.PrintSimultaneousWorldSummary(result);
        Console.WriteLine();
    }

    return 0;
}

// "battery": the 2-per-class shared-world tuning battery. Ten bots (two of
// every class) in ONE shared world via WorldSimulation.TickMultiplayer,
// each session played to last-bot death or a tick cap, launching fresh
// sessions back to back until `minutes` of wall-clock elapse — then one
// pooled report across every session (abilities, passives, economy,
// combat, equipment-on-death). Passives are pooled per class rather than
// per bot, since PassiveActivationTracker is class-keyed (see RunSimultaneous).
//
//   battery [minutes] [tickCapPerSession] [seed] [aggression] [verboseFatal]
if (string.Equals(classArg, "battery", StringComparison.OrdinalIgnoreCase))
{
    var minutes = args.Length > 1 ? double.Parse(args[1]) : 60.0;
    var tickCap = args.Length > 2 ? int.Parse(args[2]) : 15_000;
    var seed = args.Length > 3 ? long.Parse(args[3]) : 1000L;
    var batteryAggression = args.Length > 4 ? double.Parse(args[4]) : 1.0;
    var batteryVerboseFatal = args.Length > 5 && args[5] is "1" or "true";

    const int CopiesPerClass = 2;
    var batteryClasses = Enum.GetValues<CharacterClass>()
        .SelectMany(c => Enumerable.Repeat(c, CopiesPerClass))
        .ToList();

    Console.WriteLine("########################################################");
    Console.WriteLine($" Shared-world BATTERY — {CopiesPerClass} of each class ({batteryClasses.Count} bots), back-to-back sessions for {minutes:F0} min");
    Console.WriteLine($" tick cap/session {tickCap}, base seed {seed}, aggression {batteryAggression:F2}");
    Console.WriteLine("########################################################");
    Console.WriteLine();

    var sw = Stopwatch.StartNew();
    var sessions = new List<SimultaneousResult>();
    var sessionIndex = 0;
    while (sw.Elapsed.TotalMinutes < minutes)
    {
        var sessionSeed = seed + sessionIndex;
        var result = PlaytestRunner.RunSimultaneous(batteryClasses, sessionSeed, tickCap, contentDirectory, abilities, batteryAggression, batteryVerboseFatal);
        sessions.Add(result);
        sessionIndex++;

        var deaths = result.PerClass.Count(r => r.DiedDuringRun);
        var hitCap = result.PerClass.Any(r => !r.DiedDuringRun);
        Console.WriteLine($"  session {sessionIndex,-4} seed {sessionSeed,-8} {result.TotalTicks,6} ticks  {deaths,2}/{result.PerClass.Count} died{(hitCap ? "  (tick cap hit)" : "")}   [{sw.Elapsed:hh\\:mm\\:ss}]");
    }

    sw.Stop();
    Console.WriteLine();
    ReportPrinter.PrintBatteryReport(sessions, sw.Elapsed, CopiesPerClass);
    return 0;
}

var classes = string.Equals(classArg, "all", StringComparison.OrdinalIgnoreCase)
    ? Enum.GetValues<CharacterClass>().ToList()
    : [ParseClass(classArg)];

// Each class gets its own non-overlapping seed range (a large stride per
// class index) rather than every class replaying the exact same baseSeed
// .. baseSeed+runs-1 sequence. That reuse was silently correlating the
// "independent" NPC samples PrintNpcTraitEffects pools across classes:
// NpcPopulation.Spawn's RNG consumption turned out to be class-agnostic,
// so the same seed produced nearly the same NPC trait rolls regardless of
// which class was under test — an n=125 pooled sample was really only
// ~25 distinct outcomes replicated five times. A distinct seed range per
// class makes every class's world (and its NPCs) genuinely independent.
const long SeedStridePerClass = 100_000;

var allReports = new List<RunReport>();
for (var classIndex = 0; classIndex < classes.Count; classIndex++)
{
    var characterClass = classes[classIndex];
    var classBaseSeed = baseSeed + classIndex * SeedStridePerClass;

    var battery = new List<RunReport>();
    for (var i = 0; i < runs; i++)
    {
        battery.Add(PlaytestRunner.Run(characterClass, classBaseSeed + i, maxTicks, contentDirectory, abilities, aggression, verboseFatal));
    }

    ReportPrinter.Print(characterClass, battery);
    Console.WriteLine();
    allReports.AddRange(battery);
}

// NPC trait effects need pooling across every class's battery to get a
// real per-trait sample — each individual battery's NPC population
// excludes the class under test and is small (LocalPopulationTarget
// NPCs per run), so printing it once per class would mostly be noise.
if (classes.Count > 1)
{
    ReportPrinter.PrintNpcTraitEffects(allReports);
}

return 0;

static CharacterClass ParseClass(string arg)
{
    if (Enum.TryParse<CharacterClass>(arg, ignoreCase: true, out var cls))
    {
        return cls;
    }

    PrintUsage();
    Environment.Exit(1);
    throw new InvalidOperationException("unreachable");
}

static IReadOnlyList<AbilityData> LoadAbilities(string contentDirectory)
{
    try
    {
        return ContentLoader.LoadAbilities(Path.Combine(contentDirectory, "abilities.json"));
    }
    catch (ContentException)
    {
        return [];
    }
}

static void PrintUsage()
{
    Console.WriteLine("Usage: PlaytestHarness <Soldier|Doctor|Spy|Scientist|Engineer|all|simul|battery> [runs] [ticksPerRun] [seed] [aggression] [verboseFatal]");
    Console.WriteLine("  simul:   all 5 classes played simultaneously in one shared world (TickMultiplayer), 'runs' sessions, each to last-bot-death.");
    Console.WriteLine("  battery: 2 of each class (10 bots) in one shared world, back-to-back sessions for [minutes] (default 60), then one pooled report.");
    Console.WriteLine("           battery [minutes] [tickCapPerSession] [seed] [aggression] [verboseFatal]");
}
