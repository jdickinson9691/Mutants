using Mutants.Core.Characters;
using Mutants.Core.Classes;
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

    public static IReadOnlyList<Mutant> Spawn(int count, LevelMap level, IRandomSource random)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count cannot be negative.");
        }

        var classes = Enum.GetValues<CharacterClass>();
        var npcs = new List<Mutant>(count);

        for (var i = 0; i < count; i++)
        {
            var name = NamePool[i % NamePool.Length] + (i >= NamePool.Length ? $" {i / NamePool.Length + 1}" : "");
            var characterClass = classes[(int)(random.NextDouble() * classes.Length)];

            var npc = new Mutant(name, characterClass);
            npc.PlaceAt(level.Start);
            npcs.Add(npc);
        }

        return npcs;
    }
}
