using ChronTravelers.Core.Characters;
using ChronTravelers.Core.Classes;
using ChronTravelers.Core.Time;

namespace ChronTravelers.Engine.Npc;

/// <summary>
/// Spawns the NPC population — docs/GDD.md §7: "A configurable population
/// of NPC 'Travelers' exists ... each a full character with class, level,
/// inventory, and Ion pool — built on the exact same character/inventory/
/// ability code path as the human player." On the continuous timeline the
/// population is scattered across the whole of 2000–5000 rather than
/// bucketed per level: each NPC starts in a random year, fast-levelled
/// into that year's soft cap band, and thereafter roams / fights / trades
/// / year-hops against its own current year's content (see
/// <see cref="NpcController"/> and ChronTravelers.Engine.Simulation.WorldSimulation).
/// The name pool is sandbox content, not launch content.
/// </summary>
public static class NpcPopulation
{
    private static readonly string[] NamePool =
    [
        "Vex", "Corrode", "Static", "Gristle", "Ashen", "Fang", "Ratchet", "Hollow", "Ember", "Drift",
    ];

    /// <summary>How far below its year's soft cap an NPC can spawn, so a year holds a spread of strengths rather than everyone at the cap.</summary>
    private const int LevelSpreadBelowCap = 12;

    public static IReadOnlyList<Traveler> Spawn(int count, TimeWorld world, IRandomSource random)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count cannot be negative.");
        }

        var classes = Enum.GetValues<CharacterClass>();
        var npcs = new List<Traveler>(count);
        var yearSpan = TimeScale.MaxYear - TimeScale.MinYear;

        for (var i = 0; i < count; i++)
        {
            // Unique per index so leaderboard personal-bests (keyed by name) never collide.
            var name = NamePool[i % NamePool.Length] + (i >= NamePool.Length ? $" {i / NamePool.Length + 1}" : "");
            var characterClass = classes[(int)(random.NextDouble() * classes.Length)];

            var startYear = TimeScale.MinYear + (int)(random.NextDouble() * yearSpan);
            startYear = Math.Clamp(startYear, TimeScale.MinYear, TimeScale.MaxYear);

            var npc = new Traveler(name, characterClass, startingYear: startYear);
            npc.PlaceAt(world.GetYear(startYear).Map.Start);

            var cap = TimeScale.SoftLevelCapForYear(startYear);
            var floor = Math.Max(1, cap - LevelSpreadBelowCap);
            var targetLevel = floor + (int)(random.NextDouble() * (cap - floor + 1));
            for (var l = 1; l < targetLevel; l++)
            {
                npc.LevelUp();
            }

            npc.Health.Heal(npc.Health.Max);
            npc.Ions.Add(npc.Ions.Max);

            npcs.Add(npc);
        }

        return npcs;
    }
}
