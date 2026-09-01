using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Time;

namespace ChronoTravelers.Core.Tests.Time;

public class TimeWorldTests
{
    private static TimeWorld World(long seed = 4242) => TestTimeWorld.Build(seed);

    [Fact]
    public void GetYear_RejectsYearsOutsideTheTimeline()
    {
        var world = World();
        Assert.Throws<ArgumentOutOfRangeException>(() => world.GetYear(1999));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.GetYear(5001));
    }

    [Fact]
    public void GetYear_IsMemoized_SameInstanceOnRepeatCalls()
    {
        var world = World();
        Assert.Same(world.GetYear(2500), world.GetYear(2500));
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(2999)]
    [InlineData(3000)]
    [InlineData(4200)]
    [InlineData(5000)]
    public void GetYear_MapIsWellFormedAndFullyConnected(int year)
    {
        MapAssert.IsWellFormedAndFullyConnected(World().GetYear(year).Map);
    }

    [Fact]
    public void GetYear_ResolvesTheEraBand()
    {
        var world = World();
        Assert.Equal("Ruined City", world.GetYear(2000).Era.Name);
        Assert.Equal("Ruined City", world.GetYear(2999).Era.Name);
        Assert.Equal("Ashfall Wastes", world.GetYear(3000).Era.Name);
        Assert.Equal("The Chronofracture", world.GetYear(4200).Era.Name);
        Assert.Equal("The Chronofracture", world.GetYear(5000).Era.Name);
    }

    [Fact]
    public void GetYear_RosterAndTierRiseWithTheYear()
    {
        var world = World();
        var early = world.GetYear(2100);
        var late = world.GetYear(4800);

        Assert.True(late.Tier > early.Tier);

        var earlyHp = early.MonsterRoster[0]().Health.Max;
        var lateHp = late.MonsterRoster[0]().Health.Max;
        Assert.True(lateHp > earlyHp);
    }

    [Fact]
    public void GetYear_HasAWardenExactlyOnScheduleYears()
    {
        var world = World();
        var scheduleYears = world.WardenYears.ToHashSet();
        Assert.NotEmpty(scheduleYears);

        foreach (var year in scheduleYears)
        {
            var content = world.GetYear(year);
            Assert.True(content.IsWardenYear);
            Assert.NotNull(content.Warden);
        }

        // A handful of non-schedule years carry no Warden.
        var checkedNonGk = 0;
        for (var year = 2001; year <= 5000 && checkedNonGk < 20; year++)
        {
            if (scheduleYears.Contains(year))
            {
                continue;
            }

            Assert.Null(world.GetYear(year).Warden);
            checkedNonGk++;
        }
    }

    [Fact]
    public void GetYear_YieldsAGovernmentStoreAndAVacantPlayerSlot()
    {
        var content = World().GetYear(2600);

        var government = content.StoreSlots.Where(s => s.Store is { IsGovernmentRun: true }).ToList();
        var vacant = content.StoreSlots.Where(s => s.IsAvailableForPurchase).ToList();

        Assert.Single(government);
        Assert.Single(vacant);
        Assert.NotEmpty(government[0].Store!.Listings);
        Assert.True(vacant[0].PurchaseCost > 0);
    }

    [Fact]
    public void GetYear_GovernmentStoreStocksTheStapleKinds()
    {
        var listings = World().GetYear(3500).StoreSlots
            .Single(s => s.Store is { IsGovernmentRun: true }).Store!.Listings;

        Assert.Contains(listings, l => l.Item.ConsumableEffect == ConsumableEffectType.Heal);
        Assert.Contains(listings, l => l.Item.ConsumableEffect == ConsumableEffectType.BuffAttack);
        Assert.Contains(listings, l => l.Item.ConsumableEffect == ConsumableEffectType.BuffDefense);
        Assert.Contains(listings, l => l.Item.Type == ItemType.Weapon);
        Assert.Contains(listings, l => l.Item.Type == ItemType.Armor);
    }

    [Fact]
    public void SameSeed_ProducesTheSameWorld()
    {
        var a = World(seed: 100).GetYear(2717);
        var b = World(seed: 100).GetYear(2717);

        Assert.Equal(Describe(a), Describe(b));
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentWardenSchedules()
    {
        Assert.NotEqual(
            World(seed: 100).WardenYears.ToArray(),
            World(seed: 200).WardenYears.ToArray());
    }

    [Fact]
    public void Constructor_RejectsAnEraReferencingAnUnknownSpecies()
    {
        var eras = new EraTable([new EraDefinition(2000, "Bad", ["r."], ["no-such-species"], [])]);
        Assert.Throws<ArgumentException>(() =>
            new TimeWorld(1, eras,
                [new SpeciesDefinition("real", "Real", [], MonsterArchetype.Baseline, ["common"])],
                TestArchetypes()));
    }

    [Fact]
    public void Constructor_RejectsAnItemCatalogMissingAStaple()
    {
        var eras = new EraTable([new EraDefinition(2000, "E", ["r."], ["s"], ["common"])]);
        var species = new[] { new SpeciesDefinition("s", "S", [], MonsterArchetype.Baseline, ["common"]) };
        // Weapon + armour + heal + attack potion, but no defense potion.
        var incomplete = TestArchetypes().Where(a => a.Effect != ConsumableEffectType.BuffDefense).ToList();

        Assert.Throws<ArgumentException>(() => new TimeWorld(1, eras, species, incomplete));
    }

    private static IReadOnlyList<ItemArchetypeDefinition> TestArchetypes() =>
    [
        new("w", "W", ItemType.Weapon, Rarity.Common, null, ConsumableEffectType.None, 0, 0, ["common"]),
        new("a", "A", ItemType.Armor, Rarity.Common, null, ConsumableEffectType.None, 0, 0, ["common"]),
        new("h", "H", ItemType.Consumable, Rarity.Common, null, ConsumableEffectType.Heal, 10, 0, ["common"]),
        new("ba", "BA", ItemType.Consumable, Rarity.Common, null, ConsumableEffectType.BuffAttack, 3, 15, ["common"]),
        new("bd", "BD", ItemType.Consumable, Rarity.Common, null, ConsumableEffectType.BuffDefense, 3, 15, ["common"]),
    ];

    private static string Describe(YearContent content) =>
        string.Join("|", content.Map.Rooms
            .OrderBy(kv => kv.Key.North).ThenBy(kv => kv.Key.East)
            .Select(kv => $"{kv.Key}:{kv.Value.Description}"))
        + "||" + string.Join(",", content.StoreSlots.Select(s => $"{s.Name}@{s.Location}"));
}
