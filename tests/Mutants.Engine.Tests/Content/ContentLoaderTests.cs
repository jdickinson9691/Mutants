using Mutants.Core.Items;
using Mutants.Engine.Content;

namespace Mutants.Engine.Tests.Content;

public class ContentLoaderTests
{
    private const string OneItemJson = """
        [
          { "id": "rusty-shiv", "name": "Rusty Shiv", "type": "Weapon", "tier": 1, "rarity": "Common" }
        ]
        """;

    private const string TwoItemsJson = """
        [
          { "id": "rusty-shiv", "name": "Rusty Shiv", "type": "Weapon", "tier": 1, "rarity": "Common" },
          { "id": "scrap-metal", "name": "Scrap Metal", "type": "Junk", "tier": 1, "rarity": "Common" }
        ]
        """;

    [Fact]
    public void LoadItemCatalog_ParsesBasicFields()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("items.json", OneItemJson);

        var catalog = ContentLoader.LoadItemCatalog(path);

        Assert.True(catalog.ContainsKey("rusty-shiv"));
        var item = catalog["rusty-shiv"];
        Assert.Equal("Rusty Shiv", item.Name);
        Assert.Equal(ItemType.Weapon, item.Type);
        Assert.Equal(1, item.Tier);
        Assert.Equal(Rarity.Common, item.Rarity);
        Assert.Null(item.RestrictedClass);
    }

    [Fact]
    public void LoadItemCatalog_ParsesRestrictedClass()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("items.json", """
            [{ "id": "great-axe", "name": "Great Axe", "type": "Weapon", "tier": 2, "rarity": "Rare", "restrictedClass": "Warrior" }]
            """);

        var catalog = ContentLoader.LoadItemCatalog(path);

        Assert.Equal(Core.Classes.CharacterClass.Warrior, catalog["great-axe"].RestrictedClass);
    }

    [Fact]
    public void LoadItemCatalog_ParsesConsumableEffectFields()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("items.json", """
            [{ "id": "ration", "name": "Ration Pack", "type": "Consumable", "tier": 1, "rarity": "Common",
               "effect": "Heal", "effectMagnitude": 12, "effectDurationTicks": 0 }]
            """);

        var catalog = ContentLoader.LoadItemCatalog(path);

        var item = catalog["ration"];
        Assert.Equal(ConsumableEffectType.Heal, item.ConsumableEffect);
        Assert.Equal(12, item.EffectMagnitude);
        Assert.True(item.IsUsable);
    }

    [Fact]
    public void LoadItemCatalog_DefaultsEffectToNoneWhenOmitted()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("items.json", OneItemJson);

        var catalog = ContentLoader.LoadItemCatalog(path);

        Assert.Equal(ConsumableEffectType.None, catalog["rusty-shiv"].ConsumableEffect);
        Assert.False(catalog["rusty-shiv"].IsUsable);
    }

    [Fact]
    public void LoadItemCatalog_ThrowsOnUnknownEffect()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("items.json", """[{ "id": "x", "name": "X", "type": "Consumable", "tier": 1, "rarity": "Common", "effect": "Nonsense" }]""");

        Assert.Throws<ContentException>(() => ContentLoader.LoadItemCatalog(path));
    }

    [Fact]
    public void LoadItemCatalog_ThrowsOnUnknownType()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("items.json", """[{ "id": "x", "name": "X", "type": "Nonsense", "tier": 1, "rarity": "Common" }]""");

        Assert.Throws<ContentException>(() => ContentLoader.LoadItemCatalog(path));
    }

    [Fact]
    public void LoadItemCatalog_ThrowsOnUnknownRarity()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("items.json", """[{ "id": "x", "name": "X", "type": "Junk", "tier": 1, "rarity": "Nonsense" }]""");

        Assert.Throws<ContentException>(() => ContentLoader.LoadItemCatalog(path));
    }

    [Fact]
    public void LoadItemCatalog_ThrowsOnDuplicateId()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("items.json", """
            [
              { "id": "dup", "name": "A", "type": "Junk", "tier": 1, "rarity": "Common" },
              { "id": "dup", "name": "B", "type": "Junk", "tier": 1, "rarity": "Common" }
            ]
            """);

        Assert.Throws<ContentException>(() => ContentLoader.LoadItemCatalog(path));
    }

    [Fact]
    public void LoadMonsterCatalog_ParsesFieldsAndLootTable()
    {
        using var dir = new TempContentDirectory();
        var itemsPath = dir.WriteFile("items.json", TwoItemsJson);
        var items = ContentLoader.LoadItemCatalog(itemsPath);
        var monstersPath = dir.WriteFile("monsters.json", """
            [{
              "id": "scavenger", "name": "Scavenger", "tier": 1, "tags": ["undead"],
              "maxHp": 28, "attackPower": 5, "defense": 3, "speed": 9, "xpReward": 40,
              "lootTable": [
                { "itemId": "scrap-metal", "dropChance": 0.6 },
                { "itemId": "rusty-shiv", "dropChance": 0.25 }
              ]
            }]
            """);

        var catalog = ContentLoader.LoadMonsterCatalog(monstersPath, items);
        var monster = catalog["scavenger"]();

        Assert.Equal("Scavenger", monster.Name);
        Assert.Equal(1, monster.Tier);
        Assert.Equal(28, monster.Health.Max);
        Assert.Equal(5, monster.AttackPower);
        Assert.Equal(3, monster.Defense);
        Assert.Equal(9, monster.Speed);
        Assert.Equal(40, monster.XpReward);
        Assert.Equal(2, monster.LootTable.Count);
    }

    [Fact]
    public void LoadMonsterCatalog_ThrowsOnUnknownItemReference()
    {
        using var dir = new TempContentDirectory();
        var itemsPath = dir.WriteFile("items.json", OneItemJson);
        var items = ContentLoader.LoadItemCatalog(itemsPath);
        var monstersPath = dir.WriteFile("monsters.json", """
            [{
              "id": "m", "name": "M", "tier": 1, "maxHp": 10, "attackPower": 1, "defense": 1, "speed": 1, "xpReward": 1,
              "lootTable": [{ "itemId": "does-not-exist", "dropChance": 0.5 }]
            }]
            """);

        Assert.Throws<ContentException>(() => ContentLoader.LoadMonsterCatalog(monstersPath, items));
    }

    [Fact]
    public void LoadMonsterCatalog_FactoryProducesAFreshHealthPoolEachCall()
    {
        using var dir = new TempContentDirectory();
        var itemsPath = dir.WriteFile("items.json", OneItemJson);
        var items = ContentLoader.LoadItemCatalog(itemsPath);
        var monstersPath = dir.WriteFile("monsters.json", """
            [{ "id": "m", "name": "M", "tier": 1, "maxHp": 10, "attackPower": 1, "defense": 1, "speed": 1, "xpReward": 1, "lootTable": [] }]
            """);
        var catalog = ContentLoader.LoadMonsterCatalog(monstersPath, items);

        var first = catalog["m"]();
        first.Health.Damage(5);
        var second = catalog["m"]();

        Assert.Equal(10, second.Health.Current);
    }

    [Fact]
    public void LoadStoreSlots_GovernmentStoreIsStockedFromListings()
    {
        using var dir = new TempContentDirectory();
        var itemsPath = dir.WriteFile("items.json", OneItemJson);
        var items = ContentLoader.LoadItemCatalog(itemsPath);
        var storesPath = dir.WriteFile("stores.json", """
            [{
              "levelNumber": 1, "name": "Ration Depot", "location": { "east": 1, "north": 0 },
              "purchaseCost": 0, "isGovernment": true,
              "listings": [{ "itemId": "rusty-shiv", "askingPrice": 20 }]
            }]
            """);

        var byLevel = ContentLoader.LoadStoreSlots(storesPath, items);
        var slot = byLevel[1].Single();

        Assert.NotNull(slot.Store);
        Assert.True(slot.Store!.IsGovernmentRun);
        Assert.Single(slot.Store.Listings);
        Assert.Equal(20, slot.Store.Listings[0].AskingPrice);
        Assert.False(slot.IsAvailableForPurchase);
    }

    [Fact]
    public void LoadStoreSlots_NonGovernmentSlotIsPurchasableWithNoStore()
    {
        using var dir = new TempContentDirectory();
        var itemsPath = dir.WriteFile("items.json", OneItemJson);
        var items = ContentLoader.LoadItemCatalog(itemsPath);
        var storesPath = dir.WriteFile("stores.json", """
            [{ "levelNumber": 1, "name": "Empty Slot", "location": { "east": 2, "north": 2 }, "purchaseCost": 150, "isGovernment": false, "listings": [] }]
            """);

        var byLevel = ContentLoader.LoadStoreSlots(storesPath, items);
        var slot = byLevel[1].Single();

        Assert.Null(slot.Store);
        Assert.True(slot.IsAvailableForPurchase);
        Assert.Equal(150, slot.PurchaseCost);
    }

    [Fact]
    public void LoadStoreSlots_GroupsSlotsByLevelNumber()
    {
        using var dir = new TempContentDirectory();
        var itemsPath = dir.WriteFile("items.json", OneItemJson);
        var items = ContentLoader.LoadItemCatalog(itemsPath);
        var storesPath = dir.WriteFile("stores.json", """
            [
              { "levelNumber": 1, "name": "A", "location": { "east": 0, "north": 0 }, "purchaseCost": 0, "isGovernment": false, "listings": [] },
              { "levelNumber": 2, "name": "B", "location": { "east": 0, "north": 0 }, "purchaseCost": 0, "isGovernment": false, "listings": [] }
            ]
            """);

        var byLevel = ContentLoader.LoadStoreSlots(storesPath, items);

        Assert.Equal(["A"], byLevel[1].Select(s => s.Name));
        Assert.Equal(["B"], byLevel[2].Select(s => s.Name));
    }

    private static Mutants.Engine.Content.LevelData SimpleLevelData(string? gatekeeperId = null, IEnumerable<string>? rosterIds = null) => new()
    {
        LevelNumber = 1,
        Name = "Test Level",
        Start = new CoordinateData { East = 0, North = 0 },
        MinCharacterLevelToUnlock = 1,
        GatekeeperMonsterId = gatekeeperId,
        MonsterRosterIds = rosterIds?.ToList() ?? [],
        Rooms =
        [
            new RoomData { East = 0, North = 0, Description = "Start room.", Exits = new Dictionary<string, string> { ["East"] = "leads onward." } },
            new RoomData { East = 1, North = 0, Description = "East room.", Exits = new Dictionary<string, string> { ["West"] = "back to start." } },
        ],
    };

    private static IReadOnlyDictionary<string, Func<Core.Monsters.Monster>> EmptyMonsterCatalog() =>
        new Dictionary<string, Func<Core.Monsters.Monster>>();

    [Fact]
    public void BuildLevel_ParsesRoomsExitsAndStart()
    {
        var level = ContentLoader.BuildLevel(SimpleLevelData(), EmptyMonsterCatalog(), []);

        Assert.Equal(1, level.LevelNumber);
        Assert.Empty(level.Map.Validate());
        Assert.Equal(new Core.World.Coordinate(0, 0), level.Map.Start);
        var moveResult = level.Map.TryMove(level.Map.Start, Core.World.Direction.East);
        Assert.True(moveResult.Success);
    }

    [Fact]
    public void BuildLevel_ThrowsOnUnknownDirectionName()
    {
        var data = SimpleLevelData();
        data.Rooms[0].Exits["Sideways"] = "???";

        Assert.Throws<ContentException>(() => ContentLoader.BuildLevel(data, EmptyMonsterCatalog(), []));
    }

    [Fact]
    public void BuildLevel_ThrowsOnDuplicateRoomCoordinate()
    {
        var data = SimpleLevelData();
        data.Rooms.Add(new RoomData { East = 0, North = 0, Description = "Duplicate.", Exits = [] });

        Assert.Throws<ContentException>(() => ContentLoader.BuildLevel(data, EmptyMonsterCatalog(), []));
    }

    [Fact]
    public void BuildLevel_ThrowsOnUnknownMonsterRosterReference()
    {
        var data = SimpleLevelData(rosterIds: ["ghost"]);

        Assert.Throws<ContentException>(() => ContentLoader.BuildLevel(data, EmptyMonsterCatalog(), []));
    }

    [Fact]
    public void BuildLevel_ThrowsOnUnknownGatekeeperReference()
    {
        var data = SimpleLevelData(gatekeeperId: "ghost");

        Assert.Throws<ContentException>(() => ContentLoader.BuildLevel(data, EmptyMonsterCatalog(), []));
    }

    [Fact]
    public void BuildLevel_ResolvesAValidGatekeeperReference()
    {
        var monsters = new Dictionary<string, Func<Core.Monsters.Monster>>
        {
            ["boss"] = () => Core.Monsters.Monster.Create("Boss", 1),
        };
        var data = SimpleLevelData(gatekeeperId: "boss");

        var level = ContentLoader.BuildLevel(data, monsters, []);

        Assert.NotNull(level.Gatekeeper);
        Assert.Equal("Boss", level.Gatekeeper!().Name);
    }

    [Fact]
    public void LoadWorld_FullyWiresItemsMonstersStoresAndLevelsTogether()
    {
        using var dir = new TempContentDirectory();
        dir.WriteFile("items.json", TwoItemsJson);
        dir.WriteFile("monsters.json", """
            [{
              "id": "scavenger", "name": "Scavenger", "tier": 1, "maxHp": 20, "attackPower": 5, "defense": 2, "speed": 8, "xpReward": 30,
              "lootTable": [{ "itemId": "scrap-metal", "dropChance": 0.5 }]
            }]
            """);
        dir.WriteFile("stores.json", """
            [{ "levelNumber": 1, "name": "Depot", "location": { "east": 0, "north": 0 }, "purchaseCost": 0, "isGovernment": true, "listings": [{ "itemId": "rusty-shiv", "askingPrice": 10 }] }]
            """);
        dir.WriteFile("levels/level-1.json", """
            {
              "levelNumber": 1, "name": "Level One", "start": { "east": 0, "north": 0 }, "minCharacterLevelToUnlock": 1,
              "monsterRosterIds": ["scavenger"],
              "rooms": [{ "east": 0, "north": 0, "description": "Origin.", "exits": {} }]
            }
            """);

        var world = ContentLoader.LoadWorld(dir.Path);

        Assert.Equal(1, world.MaxLevel);
        var level1 = world.GetLevel(1);
        Assert.Equal("Level One", level1.Map.Name);
        Assert.Single(level1.MonsterRoster);
        Assert.Equal("Scavenger", level1.MonsterRoster[0]().Name);
        Assert.Single(level1.StoreSlots);
        Assert.Equal("Depot", level1.StoreSlots[0].Name);
    }

    [Fact]
    public void LoadWorld_ThrowsWhenLevelsDirectoryIsMissing()
    {
        using var dir = new TempContentDirectory();
        dir.WriteFile("items.json", "[]");
        dir.WriteFile("monsters.json", "[]");
        dir.WriteFile("stores.json", "[]");

        Assert.Throws<ContentException>(() => ContentLoader.LoadWorld(dir.Path));
    }

    [Fact]
    public void LoadAbilities_ParsesEntries()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("abilities.json", """
            [{ "class": "Warrior", "tier": 1, "level": 5, "name": "Cleave", "description": "Hit up to 2 additional adjacent enemies." }]
            """);

        var abilities = ContentLoader.LoadAbilities(path);

        Assert.Single(abilities);
        Assert.Equal("Cleave", abilities[0].Name);
        Assert.Equal(5, abilities[0].Level);
    }

    [Fact]
    public void LoadNpcPopulation_ParsesEntries()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("npc-population.json", """
            [{ "levelNumber": 1, "count": 5, "minLevel": 1, "maxLevel": 1 }]
            """);

        var config = ContentLoader.LoadNpcPopulation(path);

        Assert.Single(config);
        Assert.Equal(5, config[0].Count);
    }

    [Fact]
    public void LoadItemCatalog_ThrowsForMissingFile()
    {
        using var dir = new TempContentDirectory();
        Assert.Throws<ContentException>(() => ContentLoader.LoadItemCatalog(System.IO.Path.Combine(dir.Path, "nope.json")));
    }

    [Fact]
    public void LoadItemCatalog_ThrowsForInvalidJson()
    {
        using var dir = new TempContentDirectory();
        var path = dir.WriteFile("items.json", "{ this is not valid json ]");

        Assert.Throws<ContentException>(() => ContentLoader.LoadItemCatalog(path));
    }
}
