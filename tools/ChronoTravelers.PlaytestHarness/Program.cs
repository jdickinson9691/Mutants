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
// Usage: dotnet run --project tools/ChronoTravelers.PlaytestHarness -- <Class|all> [runs] [ticksPerRun] [seed] [aggression]
//   Class:       Soldier | Doctor | Spy | Scientist | Engineer | all
//   runs:        bot playthroughs per class (default 3)
//   ticksPerRun: world-tick budget per run (default 3000)
//   seed:        base world seed; run N uses seed+N (default 1000)
//   aggression:  healing-threshold multiplier, >1 = heals later/less
//                cautiously (default 1.0) — see PlaytestRunner.Run's doc
//                comment; useful for surfacing low-HP passives (Second
//                Wind, Unbreakable) the default caution rarely triggers.

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

var contentDirectory = Path.Combine(AppContext.BaseDirectory, "Content");
var abilities = LoadAbilities(contentDirectory);

var classes = string.Equals(classArg, "all", StringComparison.OrdinalIgnoreCase)
    ? Enum.GetValues<CharacterClass>().ToList()
    : [ParseClass(classArg)];

foreach (var characterClass in classes)
{
    var battery = new List<RunReport>();
    for (var i = 0; i < runs; i++)
    {
        battery.Add(PlaytestRunner.Run(characterClass, baseSeed + i, maxTicks, contentDirectory, abilities, aggression));
    }

    ReportPrinter.Print(characterClass, battery);
    Console.WriteLine();
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
    Console.WriteLine("Usage: PlaytestHarness <Soldier|Doctor|Spy|Scientist|Engineer|all> [runs] [ticksPerRun] [seed] [aggression]");
}
