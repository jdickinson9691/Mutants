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
var runs = args.Length > 1 ? int.Parse(args[1]) : 3;
var maxTicks = args.Length > 2 ? int.Parse(args[2]) : 3000;
var baseSeed = args.Length > 3 ? long.Parse(args[3]) : 1000;
var aggression = args.Length > 4 ? double.Parse(args[4]) : 1.0;
var verboseFatal = args.Length > 5 && args[5] is "1" or "true";

var contentDirectory = Path.Combine(AppContext.BaseDirectory, "Content");
var abilities = LoadAbilities(contentDirectory);

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
    Console.WriteLine("Usage: PlaytestHarness <Soldier|Doctor|Spy|Scientist|Engineer|all> [runs] [ticksPerRun] [seed] [aggression] [verboseFatal]");
}
