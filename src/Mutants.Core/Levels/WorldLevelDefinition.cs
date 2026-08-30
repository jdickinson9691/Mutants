using Mutants.Core.Economy;
using Mutants.Core.Monsters;
using Mutants.Core.World;

namespace Mutants.Core.Levels;

/// <summary>
/// One time-travel level's full content bundle — docs/GDD.md §3.2: "each
/// a separate grid map with its own room content, monster roster, loot
/// tables, and store population." <see cref="LevelNumber"/> doubles as
/// the tier used for monster/loot/store scaling throughout (§5, §6.1).
/// </summary>
public sealed class WorldLevelDefinition
{
    public int LevelNumber { get; }
    public LevelMap Map { get; }
    public IReadOnlyList<Func<Monster>> MonsterRoster { get; }
    public IReadOnlyList<StoreSlot> StoreSlots { get; }

    /// <summary>
    /// This level's boss — defeating it once is required to unlock the
    /// level (docs/GDD.md §3.2). Null for level 1, which needs no unlock.
    /// </summary>
    public Func<Monster>? Gatekeeper { get; }

    /// <summary>
    /// Minimum character level required even to attempt this level's
    /// gatekeeper — docs/GDD.md §3.2 confirms this requirement exists but
    /// not the number; original tuning pending Design Agent sign-off.
    /// </summary>
    public int MinCharacterLevelToUnlock { get; }

    public WorldLevelDefinition(
        int levelNumber,
        LevelMap map,
        IReadOnlyList<Func<Monster>> monsterRoster,
        IReadOnlyList<StoreSlot> storeSlots,
        Func<Monster>? gatekeeper = null,
        int minCharacterLevelToUnlock = 1)
    {
        if (levelNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(levelNumber), levelNumber, "Level number must be at least 1.");
        }

        if (minCharacterLevelToUnlock < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minCharacterLevelToUnlock), minCharacterLevelToUnlock, "Minimum character level must be at least 1.");
        }

        LevelNumber = levelNumber;
        Map = map;
        MonsterRoster = monsterRoster;
        StoreSlots = storeSlots;
        Gatekeeper = gatekeeper;
        MinCharacterLevelToUnlock = minCharacterLevelToUnlock;
    }
}
