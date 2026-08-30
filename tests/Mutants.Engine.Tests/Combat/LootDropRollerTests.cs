using Mutants.Core.Items;
using Mutants.Core.Monsters;
using Mutants.Engine.Combat;

namespace Mutants.Engine.Tests.Combat;

public class LootDropRollerTests
{
    private static LootTableEntry Entry(string name, double dropChance) =>
        new(Item.Create(name, ItemType.Junk, 1, Rarity.Common), dropChance);

    [Fact]
    public void Roll_ReturnsEmptyForEmptyTable()
    {
        var dropped = LootDropRoller.Roll([], StubRandomSource.Fixed(0.0));
        Assert.Empty(dropped);
    }

    [Fact]
    public void Roll_DropsEntryWhenRollBeatsChance()
    {
        var table = new[] { Entry("Scrap", dropChance: 0.5) };

        var dropped = LootDropRoller.Roll(table, StubRandomSource.Fixed(0.1)); // 0.1 < 0.5 -> drops

        Assert.Single(dropped);
        Assert.Equal("Scrap", dropped[0].Name);
    }

    [Fact]
    public void Roll_SkipsEntryWhenRollMissesChance()
    {
        var table = new[] { Entry("Scrap", dropChance: 0.5) };

        var dropped = LootDropRoller.Roll(table, StubRandomSource.Fixed(0.9)); // 0.9 >= 0.5 -> no drop

        Assert.Empty(dropped);
    }

    [Fact]
    public void Roll_EvaluatesEachEntryIndependently()
    {
        var table = new[] { Entry("Always", dropChance: 1.0), Entry("Never", dropChance: 0.01) };
        var random = new StubRandomSource(0.0, 0.99); // first roll drops "Always", second misses "Never"

        var dropped = LootDropRoller.Roll(table, random);

        Assert.Single(dropped);
        Assert.Equal("Always", dropped[0].Name);
    }
}
