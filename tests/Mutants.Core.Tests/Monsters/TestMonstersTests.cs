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

    public static IEnumerable<object[]> Factories() =>
        TestMonsters.All.Select(factory => new object[] { factory });
}
