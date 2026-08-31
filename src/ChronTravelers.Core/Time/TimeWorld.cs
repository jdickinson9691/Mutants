using ChronTravelers.Core.Economy;
using ChronTravelers.Core.Items;
using ChronTravelers.Core.Monsters;
using ChronTravelers.Core.World;

namespace ChronTravelers.Core.Time;

/// <summary>
/// The continuous 2000–5000 timeline — the replacement for the old
/// <c>GameWorld</c>'s fixed list of levels. Given a per-save
/// <see cref="WorldSeed"/>, an <see cref="EraTable"/>, and the tier-free
/// species / item-archetype catalogs, it produces a <see cref="YearContent"/>
/// for any valid year on demand (and memoizes it, so revisiting a year in
/// one session is stable). Map layouts are a pure function of the seed
/// and year — nothing about them is persisted.
/// </summary>
public sealed class TimeWorld
{
    public long WorldSeed { get; }

    private readonly EraTable _eras;
    private readonly IReadOnlyList<SpeciesDefinition> _species;
    private readonly Dictionary<string, SpeciesDefinition> _speciesById;
    private readonly IReadOnlyList<ItemArchetypeDefinition> _itemArchetypes;
    private readonly StoreStockTemplate _storeTemplate;
    private readonly WardenSchedule _wardens;
    private readonly Dictionary<int, YearContent> _cache = [];

    public TimeWorld(
        long worldSeed,
        EraTable eras,
        IReadOnlyList<SpeciesDefinition> species,
        IReadOnlyList<ItemArchetypeDefinition> itemArchetypes,
        StoreStockTemplate? storeTemplate = null)
    {
        WorldSeed = worldSeed;
        _eras = eras;
        _species = species;
        _itemArchetypes = itemArchetypes;
        _storeTemplate = storeTemplate ?? StoreStockTemplate.Default;
        _wardens = new WardenSchedule(worldSeed);

        _speciesById = new Dictionary<string, SpeciesDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var sp in species)
        {
            if (!_speciesById.TryAdd(sp.Id, sp))
            {
                throw new ArgumentException($"Duplicate species id '{sp.Id}'.", nameof(species));
            }
        }

        var archetypeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var arch in itemArchetypes)
        {
            if (!archetypeIds.Add(arch.Id))
            {
                throw new ArgumentException($"Duplicate item archetype id '{arch.Id}'.", nameof(itemArchetypes));
            }
        }

        foreach (var era in eras.Eras)
        {
            foreach (var id in era.SpeciesIds)
            {
                if (!_speciesById.ContainsKey(id))
                {
                    throw new ArgumentException($"Era '{era.Name}' references unknown species id '{id}'.", nameof(eras));
                }
            }

            foreach (var theme in era.ItemThemeTags)
            {
                if (!itemArchetypes.Any(a => a.HasTheme(theme)))
                {
                    throw new ArgumentException($"Era '{era.Name}' item theme '{theme}' matches no item archetype.", nameof(eras));
                }
            }
        }

        RequireStaple(ItemType.Weapon, a => a.Type == ItemType.Weapon, "a weapon");
        RequireStaple(ItemType.Armor, a => a.Type == ItemType.Armor, "an armour piece");
        RequireStaple(null, a => a.Effect == ConsumableEffectType.Heal, "a heal consumable");
        RequireStaple(null, a => a.Effect == ConsumableEffectType.BuffAttack, "an attack potion");
        RequireStaple(null, a => a.Effect == ConsumableEffectType.BuffDefense, "a defense potion");

        void RequireStaple(ItemType? _, Func<ItemArchetypeDefinition, bool> predicate, string what)
        {
            if (!itemArchetypes.Any(predicate))
            {
                throw new ArgumentException($"The item catalog has no {what} — government stores need one.", nameof(itemArchetypes));
            }
        }
    }

    public WardenSchedule Wardens => _wardens;

    public IReadOnlyCollection<int> WardenYears => _wardens.Years;

    public bool IsWardenYear(int year) => _wardens.IsWardenYear(year);

    public EraTable Eras => _eras;

    /// <summary>Every year <see cref="GetYear"/> has been called for this session — the set whose store slots hold live, mutable state (ownership, capital, listings).</summary>
    public IReadOnlyCollection<int> VisitedYears => _cache.Keys;

    /// <summary>The content of <paramref name="year"/> — built once, then served from cache.</summary>
    public YearContent GetYear(int year)
    {
        if (!TimeScale.IsValidYear(year))
        {
            throw new ArgumentOutOfRangeException(
                nameof(year), year, $"Year must be between {TimeScale.MinYear} and {TimeScale.MaxYear}.");
        }

        if (_cache.TryGetValue(year, out var cached))
        {
            return cached;
        }

        var built = Build(year);
        _cache[year] = built;
        return built;
    }

    private YearContent Build(int year)
    {
        var era = _eras.EraForYear(year);
        var map = YearMapFactory.Build(WorldSeed, era, year);
        var tier = TimeScale.TierForYear(year);

        var eraSpecies = era.SpeciesIds.Select(id => _speciesById[id]).ToList();

        var roster = eraSpecies
            .Select(sp => TimelineContentFactory.ForSpecies(WorldSeed, sp, year, LootPoolFor(sp, era)))
            .ToList();

        var apexRoster = eraSpecies
            .Select(sp => TimelineContentFactory.ApexForSpecies(WorldSeed, sp, year, LootPoolFor(sp, era)))
            .ToList();

        Func<Monster>? warden = _wardens.IsWardenYear(year)
            ? () => TimelineContentFactory.Warden(WorldSeed, year)
            : null;

        // A third of the grid gets a random item on year-load so a year
        // never feels empty, plus exactly one Time Shard (a weapon 25%
        // above the year's best, and a year-scaled Credit value).
        var floorLootRng = DeterministicRandom.For(WorldSeed, year, "floorloot");
        Func<Item> floorLoot = () => TimelineContentFactory.RandomFloorItem(floorLootRng, _itemArchetypes, year);
        Func<Item> timeShard = () => TimelineContentFactory.TimeShard(year, _itemArchetypes);

        var stores = BuildStores(era, year, map);
        var population = YearPopulation.Seed(WorldSeed, year, map, roster, warden, apexRoster, floorLoot, timeShard);

        return new YearContent(year, map, era, roster, stores, warden, tier, population);
    }

    private IReadOnlyList<ItemArchetypeDefinition> LootPoolFor(SpeciesDefinition species, EraDefinition era)
    {
        var themes = species.LootThemeTags.Concat(era.ItemThemeTags).ToList();
        var pool = _itemArchetypes.Where(a => a.SharesThemeWith(themes)).ToList();

        if (pool.Count == 0)
        {
            pool = _itemArchetypes.Where(a => a.SharesThemeWith(era.ItemThemeTags)).ToList();
        }

        if (pool.Count == 0)
        {
            pool = _itemArchetypes.ToList();
        }

        // Every monster's loot table wants a junk / an equippable / a
        // consumable to choose from (see TimelineContentFactory.BuildLootTable);
        // if the themed pool is missing a category, borrow the cheapest one
        // from the full catalogue so a kill can still yield each kind.
        EnsureCategory(pool, a => a.Type == ItemType.Junk);
        EnsureCategory(pool, a => a.IsEquippable && a.RestrictedClass is null);
        EnsureCategory(pool, a => a.Type == ItemType.Consumable);

        return pool;

        void EnsureCategory(List<ItemArchetypeDefinition> p, Func<ItemArchetypeDefinition, bool> predicate)
        {
            if (p.Any(predicate))
            {
                return;
            }

            var fill = _itemArchetypes
                .Where(predicate)
                .OrderBy(a => a.Rarity)
                .FirstOrDefault();
            if (fill is not null)
            {
                p.Add(fill);
            }
        }
    }

    private IReadOnlyList<StoreSlot> BuildStores(EraDefinition era, int year, LevelMap map)
    {
        var rng = DeterministicRandom.For(WorldSeed, year, "stores");
        var rooms = map.Rooms.Keys.OrderBy(c => c.North).ThenBy(c => c.East).ToList();

        var govRoom = rooms[rng.Next(rooms.Count)];
        var slotRoom = rooms.Count > 1
            ? rooms.Where(c => !c.Equals(govRoom)).ElementAt(rng.Next(rooms.Count - 1))
            : govRoom;

        var displayTier = TimelineContentFactory.DisplayTier(year);
        var themedPool = _itemArchetypes.Where(a => a.SharesThemeWith(era.ItemThemeTags)).ToList();
        if (themedPool.Count == 0)
        {
            themedPool = _itemArchetypes.ToList();
        }

        var government = Store.CreateGovernmentStore($"{era.Name} Depot", homeLevel: year);
        foreach (var archetype in StapleArchetypes(themedPool))
        {
            var item = TimelineContentFactory.ForArchetype(archetype, year);
            government.Stock(item, EconomyPricing.DefaultAskingPrice(item));
        }

        var governmentSlot = new StoreSlot(
            government.Name, govRoom, homeLevel: year, purchaseCost: 0, government);

        var playerSlot = new StoreSlot(
            $"{era.Name} Vacant Storefront",
            slotRoom,
            homeLevel: year,
            purchaseCost: _storeTemplate.PlayerSlotCostForTier(displayTier));

        return slotRoom.Equals(govRoom) ? [governmentSlot] : [governmentSlot, playerSlot];
    }

    private IEnumerable<ItemArchetypeDefinition> StapleArchetypes(IReadOnlyList<ItemArchetypeDefinition> pool)
    {
        ItemArchetypeDefinition? Pick(Func<ItemArchetypeDefinition, bool> predicate) =>
            pool.FirstOrDefault(predicate) ?? _itemArchetypes.FirstOrDefault(predicate);

        // A government depot stocks a dependable mid-grade piece, not the
        // crude bottom of the catalogue — the exotic stuff is a loot find.
        ItemArchetypeDefinition? PickEquip(Func<ItemArchetypeDefinition, bool> isKind) =>
            Pick(a => isKind(a) && a.RestrictedClass is null && a.Rarity == Rarity.Uncommon)
            ?? Pick(a => isKind(a) && a.RestrictedClass is null && a.Rarity <= Rarity.Rare)
            ?? Pick(a => isKind(a) && a.RestrictedClass is null)
            ?? Pick(isKind);

        var picks = new[]
        {
            Pick(a => a.Effect == ConsumableEffectType.Heal),
            Pick(a => a.Effect == ConsumableEffectType.BuffAttack),
            Pick(a => a.Effect == ConsumableEffectType.BuffDefense),
            PickEquip(a => a.Type == ItemType.Weapon),
            PickEquip(a => a.Type == ItemType.Armor),
            PickEquip(a => a.IsRanged),
        };

        return picks.Where(p => p is not null).Select(p => p!).Distinct();
    }
}
