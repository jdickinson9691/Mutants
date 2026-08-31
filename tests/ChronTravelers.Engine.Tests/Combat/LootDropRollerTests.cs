using ChronTravelers.Core.Items;
using ChronTravelers.Core.Monsters;
using ChronTravelers.Engine.Combat;

namespace ChronTravelers.Engine.Tests.Combat;

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

    // --- RollForKill: a defeated monster ALWAYS drops something ----------

    [Fact]
    public void RollForKill_ForcesTheLikeliestEntryWhenEveryRollMisses()
    {
        var monster = new Monster("Grub", 2, 10, 2, 0, 5, 20, lootTable:
        [
            Entry("Long Shot", dropChance: 0.05),
            Entry("Best Bet", dropChance: 0.60),
        ]);

        var dropped = LootDropRoller.RollForKill(monster, StubRandomSource.Fixed(0.99)); // all entries miss

        Assert.Single(dropped);
        Assert.Equal("Best Bet", dropped[0].Name); // the highest-chance entry is forced
    }

    [Fact]
    public void RollForKill_SynthesisesATierScaledScrapWhenTheTableIsEmpty()
    {
        var monster = new Monster("Blank", 4, 10, 2, 0, 5, 20, lootTable: []);

        var dropped = LootDropRoller.RollForKill(monster, StubRandomSource.Fixed(0.0));

        var scrap = Assert.Single(dropped);
        Assert.Equal(ItemType.Junk, scrap.Type);
        Assert.Equal(4, scrap.Tier);
    }

    [Fact]
    public void RollForKill_KeepsAGoodRollUntouched()
    {
        var monster = new Monster("Grub", 1, 10, 2, 0, 5, 20, lootTable:
            [Entry("Scrap", dropChance: 1.0), Entry("Bonus", dropChance: 1.0)]);

        var dropped = LootDropRoller.RollForKill(monster, StubRandomSource.Fixed(0.0));

        Assert.Equal(2, dropped.Count);
    }
}
