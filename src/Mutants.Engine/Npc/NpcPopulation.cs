using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.Stats;
using Mutants.Core.World;

namespace Mutants.Engine.Npc;

/// <summary>
/// Spawns a configurable NPC population — docs/GDD.md §7: "A configurable
/// population of NPC 'Mutants' exists per time-travel level, each a full
/// character with class, level, inventory, and Ion pool — built on the
/// exact same character/inventory/ability code path as the human player."
/// The name pool is sandbox content, not launch content.
/// </summary>
public static class NpcPopulation
{
    private static readonly string[] NamePool =
    [
        "Vex", "Corrode", "Static", "Gristle", "Ashen", "Fang", "Ratchet", "Hollow", "Ember", "Drift",
    ];

    /// <summary>
    /// Spawns <paramref name="count"/> NPCs "native" to
    /// <paramref name="homeLevelNumber"/> — already unlocked up through
    /// that level (so they can freely retreat to any shallower level, per
    /// Mutants.Engine.Simulation.TimeTravelResolver's rules) and placed at
    /// its map's start room. <paramref name="minCharacterLevel"/>/
    /// <paramref name="maxCharacterLevel"/> (Content.NpcPopulationData's
    /// per-level range) pick a starting character level so deeper-level
    /// NPCs aren't dropped in fresh at level 1 against that level's
    /// tougher roster — both default to 1 (a plain level-1 spawn,
    /// identical to this method's original single-level-1 behavior) if
    /// omitted. A spawn's HP/Ions are always topped up to full for its
    /// starting level, regardless of how many levels it was fast-forwarded
    /// through. Names beyond level 1 get a "L&lt;N&gt;" suffix so two NPCs
    /// spawned on different levels never share a name (leaderboard
    /// personal-bests are recorded per name).
    /// </summary>
    public static IReadOnlyList<Mutant> Spawn(
        int count,
        LevelMap level,
        IRandomSource random,
        int homeLevelNumber = 1,
        int minCharacterLevel = 1,
        int maxCharacterLevel = 1)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count cannot be negative.");
        }

        var classes = Enum.GetValues<CharacterClass>();
        var npcs = new List<Mutant>(count);
        var levelCap = Leveling.SoftLevelCap(homeLevelNumber);

        for (var i = 0; i < count; i++)
        {
            var baseName = NamePool[i % NamePool.Length] + (i >= NamePool.Length ? $" {i / NamePool.Length + 1}" : "");
            var name = homeLevelNumber == 1 ? baseName : $"{baseName} L{homeLevelNumber}";
            var characterClass = classes[(int)(random.NextDouble() * classes.Length)];

            var npc = new Mutant(name, characterClass, unlockedTimeLevel: homeLevelNumber);
            npc.SetCurrentTimeLevel(homeLevelNumber);
            npc.PlaceAt(level.Start);

            var span = Math.Max(1, maxCharacterLevel - minCharacterLevel + 1);
            var targetLevel = Math.Clamp(minCharacterLevel + (int)(random.NextDouble() * span), 1, levelCap);
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
