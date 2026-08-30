using Mutants.Core.Items;
using Mutants.Core.Monsters;

namespace Mutants.Core.Tests.Monsters;

public class MonsterTests
{
    [Fact]
    public void Create_UsesMonsterScalingBaselines()
    {
        var monster = Monster.Create("Test Beast", tier: 2);

        Assert.Equal(MonsterScaling.BaseHp(2), monster.Health.Max);
        Assert.Equal(MonsterScaling.BaseAttackPower(2), monster.AttackPower);
        Assert.Equal(MonsterScaling.BaseDefense(2), monster.Defense);
        Assert.Equal(MonsterScaling.BaseSpeed(2), monster.Speed);
        Assert.Equal(MonsterScaling.XpReward(2), monster.XpReward);
    }

    [Fact]
    public void Create_DefaultsToEmptyLootTable()
    {
        var monster = Monster.Create("Test Beast", tier: 1);
        Assert.Empty(monster.LootTable);
    }

    [Fact]
    public void Constructor_RejectsEmptyName()
    {
        Assert.Throws<ArgumentException>(() => new Monster("", 1, 10, 1, 1, 1, 1));
    }

    [Fact]
    public void Constructor_RejectsTierBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Monster("Beast", 0, 10, 1, 1, 1, 1));
    }

    [Fact]
    public void LootTableEntry_RejectsDropChanceOutOfRange()
    {
        var item = Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common);

        Assert.Throws<ArgumentOutOfRangeException>(() => new LootTableEntry(item, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LootTableEntry(item, 1.1));
    }

    [Fact]
    public void LootTableEntry_AllowsFullRangeBoundary()
    {
        var item = Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common);
        var entry = new LootTableEntry(item, 1.0);
        Assert.Equal(1.0, entry.DropChance);
    }
}
