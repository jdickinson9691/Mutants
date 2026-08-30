using Mutants.Core.Monsters;

namespace Mutants.Core.Tests.Monsters;

public class TestMonstersTests
{
    [Theory]
    [MemberData(nameof(Factories))]
    public void AllTestMonsters_AreWellFormedTierOneMonsters(Func<Monster> factory)
    {
        var monster = factory();

        Assert.Equal(1, monster.Tier);
        Assert.True(monster.Health.Max > 0);
        Assert.False(string.IsNullOrWhiteSpace(monster.Name));
        Assert.All(monster.LootTable, entry => Assert.True(entry.DropChance is > 0 and <= 1));
    }

    [Fact]
    public void Factories_ProduceFreshHealthPoolsEachCall()
    {
        var first = TestMonsters.Scavenger();
        first.Health.Damage(1);

        var second = TestMonsters.Scavenger();

        Assert.Equal(second.Health.Max, second.Health.Current);
    }

    [Fact]
    public void Factories_DefaultToTierOne()
    {
        Assert.Equal(1, TestMonsters.Scavenger().Tier);
        Assert.Equal(1, TestMonsters.JunkGolem().Tier);
        Assert.Equal(1, TestMonsters.FeralDog().Tier);
    }

    [Fact]
    public void Factories_ScaleToAnyGivenTier()
    {
        var tier3 = TestMonsters.Scavenger(tier: 3);
        Assert.Equal(3, tier3.Tier);
        Assert.All(tier3.LootTable, entry => Assert.Equal(3, entry.Item.Tier));
        Assert.True(tier3.Health.Max > TestMonsters.Scavenger(tier: 1).Health.Max);
    }

    [Fact]
    public void RosterFor_ProducesTheSameNamedMonstersAtTheGivenTier()
    {
        var roster = TestMonsters.RosterFor(2);
        Assert.Equal(3, roster.Count);
        Assert.All(roster, factory => Assert.Equal(2, factory().Tier));
    }

    [Fact]
    public void Gatekeeper_IsABulletSpongeNotAHarderHitter()
    {
        // By design (see TestMonsters.Gatekeeper's doc comment): triple
        // HP and reward, but the SAME attack/defense as a regular
        // same-tier monster - stacking every stat compounds too fast and
        // risks an unwinnable mandatory gate.
        var gatekeeper = TestMonsters.Gatekeeper(2);
        var regular = TestMonsters.Scavenger(2);

        Assert.Equal(2, gatekeeper.Tier);
        Assert.True(gatekeeper.Health.Max > regular.Health.Max);
        Assert.Equal(regular.AttackPower, gatekeeper.AttackPower);
        Assert.Equal(regular.Defense, gatekeeper.Defense);
        Assert.True(gatekeeper.XpReward > regular.XpReward);
        Assert.Single(gatekeeper.LootTable);
        Assert.Equal(1.0, gatekeeper.LootTable[0].DropChance);
    }

    public static IEnumerable<object[]> Factories() =>
        TestMonsters.All.Select(factory => new object[] { factory });
}
