using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Time;

namespace ChronoTravelers.Engine.Npc;

/// <summary>
/// Spawns the NPC population — docs/GDD.md §7: "A configurable population
/// of NPC 'Travelers' exists ... each a full character with class, level,
/// inventory, and Tachyon pool — built on the exact same character/inventory/
/// ability code path as the human player." Each NPC starts fast-levelled
/// into its spawn year's soft-cap band, then thereafter roams / fights /
/// trades / year-hops against its own current year's content (see
/// <see cref="NpcController"/> and ChronoTravelers.Engine.Simulation.WorldSimulation).
/// The first <see cref="LocalPopulationTarget"/> population slots (all of
/// them, at the default <c>totalCount</c>) are the "local pool" that
/// actively gravitates toward wherever the player currently is — see
/// <see cref="LocalPopulationTarget"/> — so their store sales, fights, and
/// chatter are things the player actually encounters rather than
/// statistically-almost-never; any slots beyond that keep the original
/// whole-timeline scatter as background flavor. The name pool is sandbox
/// content, not launch content.
/// </summary>
public static class NpcPopulation
{
    private static readonly string[] NamePool =
    [
        "Vex", "Corrode", "Static", "Gristle", "Ashen", "Fang", "Ratchet", "Hollow", "Ember", "Drift",
    ];

    /// <summary>How far below its year's soft cap an NPC can spawn, so a year holds a spread of strengths rather than everyone at the cap.</summary>
    private const int LevelSpreadBelowCap = 12;

    /// <summary>
    /// How many of the population actively gravitate toward wherever the
    /// player (or, on the shared-world server, a rotating occupied year)
    /// currently is — the "5 NPC players active [near the player] at any
    /// given time" requirement. Population indices below this count are
    /// the "local pool": <see cref="ChronoTravelers.Engine.Npc.NpcController.Act"/>
    /// pulls them hard toward the current anchor year whenever they're not
    /// already there (see its <c>pullToAnchor</c> parameter), and
    /// <see cref="RespawnNear"/> is what a dead one comes back as. Any
    /// indices at or above this count (only reachable if a designer raises
    /// `npc-population.json`'s <c>totalCount</c> past this) keep the
    /// original whole-timeline scatter behavior instead — background
    /// flavor across years the player isn't currently in, same as the
    /// pre-locality design. Default `totalCount` is exactly this number,
    /// so by default the *entire* population is the local pool.
    /// </summary>
    public const int LocalPopulationTarget = 5;

    /// <summary>How far from the anchor year a freshly (re)spawned local-pool NPC can land — a spread, not a pinpoint, so the local group doesn't all appear in the exact same room-adjacent state. <see cref="ChronoTravelers.Engine.Npc.NpcController"/>'s anchor-pull travel closes the rest of the gap over the next few ticks.</summary>
    public const int LocalSpawnSpreadYears = 60;

    public static IReadOnlyList<Traveler> Spawn(int count, TimeWorld world, IRandomSource random)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count cannot be negative.");
        }

        var yearSpan = TimeScale.MaxYear - TimeScale.MinYear;
        var npcs = new List<Traveler>(count);

        for (var i = 0; i < count; i++)
        {
            var startYear = Math.Clamp(TimeScale.MinYear + (int)(random.NextDouble() * yearSpan), TimeScale.MinYear, TimeScale.MaxYear);
            npcs.Add(Create(i, startYear, world, random));
        }

        return npcs;
    }

    /// <summary>
    /// One replacement NPC for population slot <paramref name="index"/>,
    /// landing within <see cref="LocalSpawnSpreadYears"/> of
    /// <paramref name="anchorYear"/> (clamped to the timeline) — used for a
    /// dead local-pool member (see <see cref="LocalPopulationTarget"/>) so
    /// it comes back somewhere the player can actually run into it, rather
    /// than a fresh whole-timeline random draw that could land it 3000
    /// years away again.
    /// </summary>
    public static Traveler RespawnNear(int index, int anchorYear, TimeWorld world, IRandomSource random)
    {
        var low = Math.Max(TimeScale.MinYear, anchorYear - LocalSpawnSpreadYears);
        var high = Math.Min(TimeScale.MaxYear, anchorYear + LocalSpawnSpreadYears);
        var startYear = low + (int)(random.NextDouble() * (high - low + 1));
        return Create(index, startYear, world, random);
    }

    /// <summary>One replacement NPC for population slot <paramref name="index"/>, drawn from the whole timeline exactly like the initial <see cref="Spawn"/> — for a dead background-pool member (index at or beyond <see cref="LocalPopulationTarget"/>).</summary>
    public static Traveler RespawnScattered(int index, TimeWorld world, IRandomSource random)
    {
        var yearSpan = TimeScale.MaxYear - TimeScale.MinYear;
        var startYear = Math.Clamp(TimeScale.MinYear + (int)(random.NextDouble() * yearSpan), TimeScale.MinYear, TimeScale.MaxYear);
        return Create(index, startYear, world, random);
    }

    /// <summary>
    /// Builds one NPC for population slot <paramref name="index"/>, starting
    /// in <paramref name="startYear"/> — the name (stable per index, so
    /// leaderboard personal-bests and "who"/broadcast identity survive a
    /// respawn), class, fast-levelling into that year's soft-cap band, and
    /// full HP/Tachyons are all identical to the original single-spawn-pass
    /// behavior; only where the year comes from differs between the three
    /// public entry points above.
    /// </summary>
    private static Traveler Create(int index, int startYear, TimeWorld world, IRandomSource random)
    {
        var classes = Enum.GetValues<CharacterClass>();

        // Unique per index so leaderboard personal-bests (keyed by name) never collide.
        var name = NamePool[index % NamePool.Length] + (index >= NamePool.Length ? $" {index / NamePool.Length + 1}" : "");
        var characterClass = classes[(int)(random.NextDouble() * classes.Length)];

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
        npc.Tachyons.Add(npc.Tachyons.Max, respectSoftCap: true); // top off to the nominal pool, not past it

        return npc;
    }
}
