using System.Text.Json;
using Mutants.Core.Classes;
using Mutants.Core.Economy;
using Mutants.Core.Items;
using Mutants.Core.Levels;
using Mutants.Core.Monsters;
using Mutants.Core.Time;
using Mutants.Core.World;

namespace Mutants.Engine.Content;

/// <summary>
/// Turns Mutants.Content's JSON files into live Core domain objects —
/// docs/TECH_STACK.md: "Data-driven content ... JSON or YAML content
/// files loaded at startup, deserialized with System.Text.Json." Content
/// data owns names/ids/numbers (docs/AGENTS.md's Content Agent); this
/// class owns turning that into the same domain types the rest of the
/// engine already works with (Item, Monster, LevelMap, Store, GameWorld),
/// so nothing downstream needs to know content came from JSON at all.
/// </summary>
public static class ContentLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyDictionary<string, Item> LoadItemCatalog(string path)
    {
        var templates = ReadJson<List<ItemTemplateData>>(path);
        var catalog = new Dictionary<string, Item>();

        foreach (var template in templates)
        {
            if (!Enum.TryParse<ItemType>(template.Type, ignoreCase: true, out var type))
            {
                throw new ContentException($"Item '{template.Id}': unknown type '{template.Type}'.");
            }

            if (!Enum.TryParse<Rarity>(template.Rarity, ignoreCase: true, out var rarity))
            {
                throw new ContentException($"Item '{template.Id}': unknown rarity '{template.Rarity}'.");
            }

            CharacterClass? restrictedClass = null;
            if (template.RestrictedClass is not null)
            {
                if (!Enum.TryParse<CharacterClass>(template.RestrictedClass, ignoreCase: true, out var parsed))
                {
                    throw new ContentException($"Item '{template.Id}': unknown restrictedClass '{template.RestrictedClass}'.");
                }

                restrictedClass = parsed;
            }

            if (!Enum.TryParse<ConsumableEffectType>(template.Effect, ignoreCase: true, out var effect))
            {
                throw new ContentException($"Item '{template.Id}': unknown effect '{template.Effect}'.");
            }

            var item = Item.Create(template.Name, type, template.Tier, rarity, restrictedClass, effect, template.EffectMagnitude, template.EffectDurationTicks);
            if (!catalog.TryAdd(template.Id, item))
            {
                throw new ContentException($"Duplicate item id '{template.Id}'.");
            }
        }

        return catalog;
    }

    public static IReadOnlyDictionary<string, Func<Monster>> LoadMonsterCatalog(string path, IReadOnlyDictionary<string, Item> items)
    {
        var templates = ReadJson<List<MonsterTemplateData>>(path);
        var catalog = new Dictionary<string, Func<Monster>>();

        foreach (var template in templates)
        {
            // Built once per template (immutable, so safe to reuse across
            // every Monster instance the factory below creates) - only the
            // Monster itself (and its mutable HealthPool) needs to be
            // fresh per fight.
            var lootTable = template.LootTable
                .Select(entry => new LootTableEntry(ResolveItem(items, entry.ItemId, $"monster '{template.Id}'"), entry.DropChance))
                .ToList();

            var name = template.Name;
            var tier = template.Tier;
            var maxHp = template.MaxHp;
            var attackPower = template.AttackPower;
            var defense = template.Defense;
            var speed = template.Speed;
            var xpReward = template.XpReward;
            var tags = template.Tags;

            if (!catalog.TryAdd(template.Id, () => new Monster(name, tier, maxHp, attackPower, defense, speed, xpReward, lootTable, tags)))
            {
                throw new ContentException($"Duplicate monster id '{template.Id}'.");
            }
        }

        return catalog;
    }

    /// <summary>Loads every store slot, grouped by the time-travel level it belongs to.</summary>
    public static IReadOnlyDictionary<int, List<StoreSlot>> LoadStoreSlots(string path, IReadOnlyDictionary<string, Item> items)
    {
        var templates = ReadJson<List<StoreSlotData>>(path);
        var byLevel = new Dictionary<int, List<StoreSlot>>();

        foreach (var template in templates)
        {
            Store? store = null;
            if (template.IsGovernment)
            {
                store = Store.CreateGovernmentStore(template.Name, template.LevelNumber);
                foreach (var listing in template.Listings)
                {
                    store.Stock(ResolveItem(items, listing.ItemId, $"store '{template.Name}'"), listing.AskingPrice);
                }
            }

            var slot = new StoreSlot(
                template.Name,
                new Coordinate(template.Location.East, template.Location.North),
                template.LevelNumber,
                template.PurchaseCost,
                store);

            if (!byLevel.TryGetValue(template.LevelNumber, out var slotsForLevel))
            {
                slotsForLevel = [];
                byLevel[template.LevelNumber] = slotsForLevel;
            }

            slotsForLevel.Add(slot);
        }

        return byLevel;
    }

    public static WorldLevelDefinition BuildLevel(
        LevelData data,
        IReadOnlyDictionary<string, Func<Monster>> monsters,
        IReadOnlyList<StoreSlot> storeSlotsForThisLevel)
    {
        var start = new Coordinate(data.Start.East, data.Start.North);
        var rooms = new Dictionary<Coordinate, Room>();

        foreach (var roomData in data.Rooms)
        {
            var coordinate = new Coordinate(roomData.East, roomData.North);
            var exits = new List<(Direction Direction, string Text)>();

            foreach (var (directionName, text) in roomData.Exits)
            {
                var direction = DirectionExtensions.Parse(directionName)
                    ?? throw new ContentException($"Level {data.LevelNumber}: unknown direction '{directionName}' in room {coordinate}.");
                exits.Add((direction, text));
            }

            if (!rooms.TryAdd(coordinate, Room.Create(roomData.Description, exits.ToArray())))
            {
                throw new ContentException($"Level {data.LevelNumber}: duplicate room at {coordinate}.");
            }
        }

        var map = new LevelMap(data.Name, start, rooms);
        var roster = data.MonsterRosterIds
            .Select(id => ResolveMonster(monsters, id, $"level '{data.Name}'"))
            .ToList();
        var gatekeeper = data.GatekeeperMonsterId is null
            ? null
            : ResolveMonster(monsters, data.GatekeeperMonsterId, $"level '{data.Name}'");

        return new WorldLevelDefinition(data.LevelNumber, map, roster, storeSlotsForThisLevel, gatekeeper, data.MinCharacterLevelToUnlock);
    }

    /// <summary>
    /// Loads the full world from a content directory expected to contain
    /// items.json, monsters.json, stores.json, and a levels/ subfolder of
    /// one JSON file per level.
    /// </summary>
    public static GameWorld LoadWorld(string contentDirectory)
    {
        var items = LoadItemCatalog(Path.Combine(contentDirectory, "items.json"));
        var monsters = LoadMonsterCatalog(Path.Combine(contentDirectory, "monsters.json"), items);
        var storeSlotsByLevel = LoadStoreSlots(Path.Combine(contentDirectory, "stores.json"), items);

        var levelsDirectory = Path.Combine(contentDirectory, "levels");
        if (!Directory.Exists(levelsDirectory))
        {
            throw new ContentException($"Levels directory not found: {levelsDirectory}");
        }

        var levels = Directory.GetFiles(levelsDirectory, "*.json")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(file => ReadJson<LevelData>(file))
            .OrderBy(data => data.LevelNumber)
            .Select(data => BuildLevel(data, monsters, storeSlotsByLevel.GetValueOrDefault(data.LevelNumber, [])))
            .ToList();

        return new GameWorld(levels);
    }

    /// <summary>
    /// Loads the continuous-timeline world (Mutants.Core.Time) from a
    /// content directory expected to contain <c>monster-species.json</c>,
    /// <c>item-archetypes.json</c>, <c>eras.json</c>, and (optionally)
    /// <c>store-templates.json</c>. <paramref name="worldSeed"/> is the
    /// per-save seed that fixes the Gatekeeper schedule and every year's
    /// map/store layout. Throws <see cref="ContentException"/> on a
    /// missing/malformed file, an unknown enum value, or a failed
    /// cross-reference (the <see cref="EraTable"/> / <see cref="TimeWorld"/>
    /// validation surfaced as content errors).
    /// </summary>
    public static TimeWorld LoadTimeWorld(string contentDirectory, long worldSeed)
    {
        var species = ReadJson<List<MonsterSpeciesData>>(Path.Combine(contentDirectory, "monster-species.json"))
            .Select(ToSpecies)
            .ToList();

        var archetypes = ReadJson<List<ItemArchetypeData>>(Path.Combine(contentDirectory, "item-archetypes.json"))
            .Select(ToArchetype)
            .ToList();

        var eras = ReadJson<List<EraData>>(Path.Combine(contentDirectory, "eras.json"))
            .Select(ToEra)
            .ToList();

        var templatePath = Path.Combine(contentDirectory, "store-templates.json");
        var template = File.Exists(templatePath) ? ReadJson<StoreTemplateData>(templatePath) : new StoreTemplateData();
        var storeTemplate = new StoreStockTemplate(template.PlayerSlotBaseCost, template.PlayerSlotCostPerTier);

        EraTable eraTable;
        try
        {
            eraTable = new EraTable(eras);
        }
        catch (ArgumentException ex)
        {
            throw new ContentException($"eras.json: {ex.Message}", ex);
        }

        try
        {
            return new TimeWorld(worldSeed, eraTable, species, archetypes, storeTemplate);
        }
        catch (ArgumentException ex)
        {
            throw new ContentException($"timeline content is inconsistent: {ex.Message}", ex);
        }
    }

    private static SpeciesDefinition ToSpecies(MonsterSpeciesData data)
    {
        if (!Enum.TryParse<MonsterArchetype>(data.Archetype, ignoreCase: true, out var archetype))
        {
            throw new ContentException($"Species '{data.Id}': unknown archetype '{data.Archetype}'.");
        }

        return new SpeciesDefinition(data.Id, data.Name, data.Tags, archetype, data.LootThemeTags);
    }

    private static ItemArchetypeDefinition ToArchetype(ItemArchetypeData data)
    {
        if (!Enum.TryParse<ItemType>(data.Type, ignoreCase: true, out var type))
        {
            throw new ContentException($"Item archetype '{data.Id}': unknown type '{data.Type}'.");
        }

        if (!Enum.TryParse<Rarity>(data.Rarity, ignoreCase: true, out var rarity))
        {
            throw new ContentException($"Item archetype '{data.Id}': unknown rarity '{data.Rarity}'.");
        }

        CharacterClass? restrictedClass = null;
        if (data.RestrictedClass is not null)
        {
            if (!Enum.TryParse<CharacterClass>(data.RestrictedClass, ignoreCase: true, out var parsed))
            {
                throw new ContentException($"Item archetype '{data.Id}': unknown restrictedClass '{data.RestrictedClass}'.");
            }

            restrictedClass = parsed;
        }

        if (!Enum.TryParse<ConsumableEffectType>(data.Effect, ignoreCase: true, out var effect))
        {
            throw new ContentException($"Item archetype '{data.Id}': unknown effect '{data.Effect}'.");
        }

        return new ItemArchetypeDefinition(
            data.Id, data.Name, type, rarity, restrictedClass,
            effect, data.EffectMagnitude, data.EffectDurationTicks, data.ThemeTags);
    }

    private static EraDefinition ToEra(EraData data) =>
        new(data.FromYear, data.Name, data.RoomText, data.SpeciesIds, data.ItemThemeTags);

    public static IReadOnlyList<AbilityData> LoadAbilities(string path) => ReadJson<List<AbilityData>>(path);

    public static IReadOnlyList<NpcPopulationData> LoadNpcPopulation(string path) => ReadJson<List<NpcPopulationData>>(path);

    private static T ReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new ContentException($"Content file not found: {path}");
        }

        using var stream = File.OpenRead(path);
        try
        {
            return JsonSerializer.Deserialize<T>(stream, JsonOptions)
                ?? throw new ContentException($"'{path}' deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new ContentException($"'{path}' is not valid JSON for the expected shape: {ex.Message}", ex);
        }
    }

    private static Item ResolveItem(IReadOnlyDictionary<string, Item> items, string id, string context) =>
        items.TryGetValue(id, out var item) ? item : throw new ContentException($"{context} references unknown item id '{id}'.");

    private static Func<Monster> ResolveMonster(IReadOnlyDictionary<string, Func<Monster>> monsters, string id, string context) =>
        monsters.TryGetValue(id, out var factory) ? factory : throw new ContentException($"{context} references unknown monster id '{id}'.");
}
