using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Monsters;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Core.World;

namespace ChronoTravelers.Core.Tests.Time;

public class YearPopulationTests
{
    private static YearPopulation PopulationFor(long seed, int year) =>
        TestTimeWorld.Build(seed).GetYear(year).Population;

    [Fact]
    public void Seed_PlacesMaxOfFourOrTwoFifthsOfTheRoomsInDistinctNonStartRooms()
    {
        var content = TestTimeWorld.Build(seed: 777).GetYear(2200);
        var pop = content.Population;
        var nonStartRooms = content.Map.RoomCount - 1;
        var expected = System.Math.Min(nonStartRooms, System.Math.Max(4, content.Map.RoomCount * 2 / 5));

        // The regular roster is the soft cap; a year may also seed an apex
        // or two on top (Monster.IsApex), placed in further distinct rooms.
        Assert.Equal(expected, pop.Monsters.Count(m => !m.IsApex));
        Assert.Equal(expected, pop.SoftCap);
        Assert.All(pop.Monsters, m => Assert.True(content.Map.Rooms.ContainsKey(m.Position)));
        Assert.DoesNotContain(pop.Monsters, m => m.Position.Equals(content.Map.Start));
        Assert.Equal(pop.Monsters.Count, pop.Monsters.Select(m => m.Position).Distinct().Count());
    }

    [Fact]
    public void Seed_IsDeterministicForTheSameSeedAndYear()
    {
        var a = PopulationFor(42, 2600);
        var b = PopulationFor(42, 2600);

        Assert.Equal(
            a.Monsters.Select(m => $"{m.Name}@{m.Position}"),
            b.Monsters.Select(m => $"{m.Name}@{m.Position}"));
    }

    [Fact]
    public void Seed_DiffersAcrossSeedsAndYears()
    {
        string Describe(YearPopulation p) => string.Join("|", p.Monsters.Select(m => $"{m.Name}@{m.Position}"));

        var baseline = Describe(PopulationFor(42, 2600));
        Assert.NotEqual(baseline, Describe(PopulationFor(43, 2600)));
        Assert.NotEqual(baseline, Describe(PopulationFor(42, 2601)));
    }

    [Fact]
    public void GetYear_ReturnsTheSameLivePopulationInstanceOnEveryCall()
    {
        var world = TestTimeWorld.Build(seed: 5);
        Assert.Same(world.GetYear(3000).Population, world.GetYear(3000).Population);
    }

    [Fact]
    public void Seed_StationsTheWardenAtTheStartRoomInAWardenYearOnly()
    {
        var world = TestTimeWorld.Build(seed: 99);
        var gkYear = world.WardenYears.First();

        var gkContent = world.GetYear(gkYear);
        Assert.NotNull(gkContent.Population.Warden);
        Assert.Equal(gkContent.Map.Start, gkContent.Population.Warden!.Position);
        Assert.DoesNotContain(gkContent.Population.Monsters, m => ReferenceEquals(m, gkContent.Population.Warden));

        var plainYear = Enumerable.Range(2001, 200).First(y => !world.IsWardenYear(y));
        Assert.Null(world.GetYear(plainYear).Population.Warden);
    }

    [Fact]
    public void Seed_PlacesApexMonstersFromTheApexRoster_InTheirOwnRooms()
    {
        // Scan a run of years; across them, the real world seeds some apexes.
        var world = TestTimeWorld.Build(seed: 424242);
        var apexes = Enumerable.Range(2000, 400)
            .SelectMany(y => world.GetYear(y).Population.Monsters)
            .Where(m => m.IsApex)
            .ToList();

        Assert.NotEmpty(apexes);
        Assert.All(apexes, a => Assert.StartsWith("Frayed ", a.Name));

        // In any single year, the apex (if present) sits in a distinct room.
        foreach (var y in Enumerable.Range(2000, 400))
        {
            var pop = world.GetYear(y).Population;
            Assert.Equal(pop.Monsters.Count, pop.Monsters.Select(m => m.Position).Distinct().Count());
        }
    }

    [Fact]
    public void Seed_WithNoApexRoster_PlacesNoApexes()
    {
        var map = TestTimeWorld.Build(1).GetYear(2000).Map;
        var roster = new List<Func<Monster>> { () => Monster.Create("Grunt", 1) };

        var pop = YearPopulation.Seed(worldSeed: 5, year: 2000, map, roster, wardenFactory: null, apexRoster: null);

        Assert.DoesNotContain(pop.Monsters, m => m.IsApex);
    }

    [Fact]
    public void MonstersAt_And_HasLivingMonsterAt_TrackPositionAndDeath()
    {
        var pop = PopulationFor(1234, 2400);
        var occupied = pop.Monsters[0].Position;

        Assert.True(pop.HasLivingMonsterAt(occupied));
        Assert.Contains(pop.Monsters[0], pop.MonstersAt(occupied));

        pop.Monsters[0].Health.Damage(pop.Monsters[0].Health.Max);
        Assert.DoesNotContain(pop.Monsters[0], pop.MonstersAt(occupied));
    }

    [Fact]
    public void GroundLoot_AddLootAtTakeRoundTrip()
    {
        // A bare population (no loot factories) so the floor starts empty.
        var map = TestTimeWorld.Build(1).GetYear(2000).Map;
        var pop = YearPopulation.Seed(worldSeed: 1, year: 2000, map, roster: [], wardenFactory: null);
        var spot = new Coordinate(0, 0);
        var junk = Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common);
        var blade = Item.Create("Blade", ItemType.Weapon, 1, Rarity.Common);

        pop.AddGroundLoot(spot, junk);
        pop.AddGroundLoot(spot, blade);
        Assert.Equal(2, pop.LootAt(spot).Count);

        var taken = pop.TakeGroundLoot(spot, i => i.Type == ItemType.Weapon);
        Assert.Same(blade, taken);
        Assert.Single(pop.LootAt(spot));

        Assert.Same(junk, pop.TakeGroundLoot(spot, _ => true));
        Assert.Empty(pop.LootAt(spot));
        Assert.Null(pop.TakeGroundLoot(spot, _ => true));
    }

    [Fact]
    public void Seed_ScattersFloorLootOverAboutAThirdOfTheGrid_PlusExactlyOneTimeShard()
    {
        var content = TestTimeWorld.Build(seed: 314).GetYear(2600);
        var pop = content.Population;

        var roomsWithLoot = content.Map.Rooms.Keys.Count(c => pop.LootAt(c).Count > 0);
        var expectedItemRooms = System.Math.Max(1, (int)System.Math.Round(content.Map.RoomCount / 3.0));

        // ~a third get an item, plus one more for the shard (allow the shard
        // room to coincide with nothing else).
        Assert.InRange(roomsWithLoot, expectedItemRooms, expectedItemRooms + 1);

        var shards = content.Map.Rooms.Keys
            .SelectMany(c => pop.LootAt(c))
            .Where(i => i.IsTimeShard)
            .ToList();
        Assert.Single(shards);
        Assert.Equal(ItemType.Weapon, shards[0].Type);
    }

    [Fact]
    public void Seed_FloorLootIsDeterministicForASeedAndYear()
    {
        static string Describe(int seed)
        {
            var content = TestTimeWorld.Build(seed).GetYear(2600);
            return string.Join("|", content.Map.Rooms.Keys
                .OrderBy(k => k.North).ThenBy(k => k.East)
                .SelectMany(k => content.Population.LootAt(k).Select(i => $"{k}:{i.Name}")));
        }

        Assert.Equal(Describe(99), Describe(99));
        Assert.NotEqual(Describe(99), Describe(100));
    }

    [Fact]
    public void AddAndRemoveMonster_MutateTheLivePopulation()
    {
        var pop = PopulationFor(2, 2100);
        var before = pop.Monsters.Count;
        var extra = Monster.Create("Interloper", tier: 1);
        extra.PlaceAt(new Coordinate(0, 0));

        pop.AddMonster(extra);
        Assert.Equal(before + 1, pop.Monsters.Count);

        pop.RemoveMonster(extra);
        Assert.Equal(before, pop.Monsters.Count);
    }
}
