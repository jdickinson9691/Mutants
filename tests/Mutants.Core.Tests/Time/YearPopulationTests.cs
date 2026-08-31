using Mutants.Core.Items;
using Mutants.Core.Monsters;
using Mutants.Core.Time;
using Mutants.Core.World;

namespace Mutants.Core.Tests.Time;

public class YearPopulationTests
{
    private static YearPopulation PopulationFor(long seed, int year) =>
        TestTimeWorld.Build(seed).GetYear(year).Population;

    [Fact]
    public void Seed_PlacesMaxOfTwoOrAThirdOfTheRoomsInDistinctNonStartRooms()
    {
        var content = TestTimeWorld.Build(seed: 777).GetYear(2200);
        var pop = content.Population;
        var expected = System.Math.Max(2, content.Map.RoomCount / 3);

        Assert.Equal(expected, pop.Monsters.Count);
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
    public void Seed_StationsTheGatekeeperAtTheStartRoomInAGatekeeperYearOnly()
    {
        var world = TestTimeWorld.Build(seed: 99);
        var gkYear = world.GatekeeperYears.First();

        var gkContent = world.GetYear(gkYear);
        Assert.NotNull(gkContent.Population.Gatekeeper);
        Assert.Equal(gkContent.Map.Start, gkContent.Population.Gatekeeper!.Position);
        Assert.DoesNotContain(gkContent.Population.Monsters, m => ReferenceEquals(m, gkContent.Population.Gatekeeper));

        var plainYear = Enumerable.Range(2001, 200).First(y => !world.IsGatekeeperYear(y));
        Assert.Null(world.GetYear(plainYear).Population.Gatekeeper);
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
        var pop = PopulationFor(1, 2000);
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
