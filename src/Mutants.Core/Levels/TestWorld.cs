using Mutants.Core.Economy;
using Mutants.Core.Items;
using Mutants.Core.Monsters;
using Mutants.Core.World;

namespace Mutants.Core.Levels;

/// <summary>
/// A small 3-level sandbox world for engine/console use, mirroring
/// World.TestLevel / Monsters.TestMonsters / Economy.TestStores. NOT
/// launch content — docs/CONTENT_PLAN.md calls for 5–8 fully realized
/// levels at launch; this exercises the time-travel mechanism itself,
/// not final level design.
/// </summary>
public static class TestWorld
{
    public static GameWorld Build()
    {
        var level1 = new WorldLevelDefinition(
            levelNumber: 1,
            map: TestLevel.Build(),
            monsterRoster: TestMonsters.RosterFor(1),
            storeSlots: TestStores.Build());

        var level2 = new WorldLevelDefinition(
            levelNumber: 2,
            map: BuildLevel2Map(),
            monsterRoster: TestMonsters.RosterFor(2),
            storeSlots: BuildLevel2Stores(),
            gatekeeper: () => TestMonsters.Gatekeeper(2),
            minCharacterLevelToUnlock: 5);

        var level3 = new WorldLevelDefinition(
            levelNumber: 3,
            map: BuildLevel3Map(),
            monsterRoster: TestMonsters.RosterFor(3),
            storeSlots: [], // no store content yet at this depth - future Content Agent work
            gatekeeper: () => TestMonsters.Gatekeeper(3),
            minCharacterLevelToUnlock: 10);

        return new GameWorld([level1, level2, level3]);
    }

    private static LevelMap BuildLevel2Map()
    {
        var descriptions = new Dictionary<Coordinate, string>
        {
            [new Coordinate(0, 0)] = "Flickering neon signage buzzes over a flooded platform.",
            [new Coordinate(1, 0)] = "Rusted turnstiles block a half-collapsed tunnel.",
            [new Coordinate(0, 1)] = "A vending machine hums, selling nothing anyone recognizes.",
            [new Coordinate(1, 1)] = "Graffiti tags cover every surface of this transit hub.",
        };

        return GridLevelBuilder.Build("Level 2 — Neon Undercity", Coordinate.Origin, descriptions);
    }

    private static LevelMap BuildLevel3Map()
    {
        var descriptions = new Dictionary<Coordinate, string>
        {
            [new Coordinate(0, 0)] = "Ash drifts in slow spirals over a dead highway.",
            [new Coordinate(1, 0)] = "A skeletal billboard creaks in the wind.",
            [new Coordinate(0, 1)] = "Sand has swallowed half of what was once a plaza.",
            [new Coordinate(1, 1)] = "Something moves beneath the dunes.",
        };

        return GridLevelBuilder.Build("Level 3 — Ashfall Wastes", Coordinate.Origin, descriptions);
    }

    private static IReadOnlyList<StoreSlot> BuildLevel2Stores()
    {
        var outpost = Store.CreateGovernmentStore("Tunnel Outpost", homeLevel: 2);
        foreach (var item in new[]
                 {
                     Item.Create("Sealed Nutrient Pack", ItemType.Consumable, 2, Rarity.Common),
                     Item.Create("Riot Baton", ItemType.Weapon, 2, Rarity.Common),
                 })
        {
            outpost.Stock(item, EconomyPricing.DefaultAskingPrice(item));
        }

        return [new StoreSlot("Tunnel Outpost", new Coordinate(0, 1), homeLevel: 2, purchaseCost: 0, outpost)];
    }
}
