using ChronTravelers.Core.Items;
using ChronTravelers.Core.Monsters;
using ChronTravelers.Core.World;

namespace ChronTravelers.Core.Tests.Monsters;

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
        Assert.Equal(MonsterScaling.BaseIons(2), monster.Ions.Max);
        Assert.Equal(monster.Ions.Max, monster.Ions.Current);
    }

    [Fact]
    public void Constructor_ExplicitMaxIonsOverridesTheScaledDefault()
    {
        var monster = new Monster("Beast", 3, 40, 5, 3, 10, 60, maxIons: 99);
        Assert.Equal(99, monster.Ions.Max);
    }

    [Fact]
    public void PendingDefensePenalty_DefaultsToZero()
    {
        Assert.Equal(0, Monster.Create("Beast", 1).PendingDefensePenalty);
    }

    [Fact]
    public void Aggro_DefaultsToZero_RaisesClampedToCap_AndDecaysNeverBelowZero()
    {
        var m = Monster.Create("Beast", 1);
        Assert.Equal(0, m.Aggro);

        m.RaiseAggro(AggroModel.Cap + 100);
        Assert.Equal(AggroModel.Cap, m.Aggro);

        m.DecayAggro(AggroModel.Cap + 100);
        Assert.Equal(0, m.Aggro);

        m.RaiseAggro(-5);          // negatives are ignored, not subtracted
        Assert.Equal(0, m.Aggro);
    }

    [Theory]
    [InlineData(0.0, AggroMood.Calm)]
    [InlineData(2.9, AggroMood.Calm)]
    [InlineData(3.0, AggroMood.Alert)]
    [InlineData(5.9, AggroMood.Alert)]
    [InlineData(6.0, AggroMood.Hostile)]
    [InlineData(12.0, AggroMood.Hostile)]
    public void AggroModel_MoodFor_BandsTheMeter(double aggro, AggroMood expected)
    {
        Assert.Equal(expected, AggroModel.MoodFor(aggro));
    }

    [Fact]
    public void PlaceAt_And_MoveTo_UpdatePosition()
    {
        var monster = Monster.Create("Beast", 1);
        Assert.Equal(Coordinate.Origin, monster.Position);

        monster.PlaceAt(new Coordinate(2, -1));
        Assert.Equal(new Coordinate(2, -1), monster.Position);

        monster.MoveTo(new Coordinate(2, 0));
        Assert.Equal(new Coordinate(2, 0), monster.Position);
    }

    [Fact]
    public void Inventory_AddAndRemove()
    {
        var monster = Monster.Create("Beast", 1);
        var item = Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common);

        monster.AddToInventory(item);
        Assert.Contains(item, monster.Inventory);

        Assert.True(monster.RemoveFromInventory(item));
        Assert.Empty(monster.Inventory);
        Assert.False(monster.RemoveFromInventory(item));
    }

    [Fact]
    public void Heal_SpendsIonsAtTheHealRatioCappedByMissingHpAndAvailableIons()
    {
        var monster = new Monster("Beast", 2, maxHp: 40, attackPower: 5, defense: 3, speed: 10, xpReward: 80, maxIons: 20);
        monster.Health.Damage(9); // exactly 3 Ions' worth at 3:1

        var healed = monster.Heal();

        Assert.Equal(9, healed);
        Assert.Equal(monster.Health.Max, monster.Health.Current);
        Assert.Equal(17, monster.Ions.Current); // spent 3

        // Now drain Ions and confirm heal no-ops without spending.
        monster.Health.Damage(20);
        monster.Ions.Spend(monster.Ions.Current);
        Assert.Equal(0, monster.Heal());
    }

    [Fact]
    public void Convert_DestroysACarriedItemForIons()
    {
        var monster = new Monster("Beast", 2, maxHp: 40, attackPower: 5, defense: 3, speed: 10, xpReward: 80, maxIons: 200);
        monster.Ions.Spend(monster.Ions.Current); // headroom to observe the gain
        var item = Item.Create("Neon Shard", ItemType.Junk, 3, Rarity.Rare);
        monster.AddToInventory(item);

        var gained = monster.Convert(item);

        Assert.True(gained > 0);
        Assert.Equal(gained, monster.Ions.Current);
        Assert.Empty(monster.Inventory);
        Assert.Throws<InvalidOperationException>(() => monster.Convert(item));
    }

    [Fact]
    public void AdvanceIonRegenTick_AddsOneIonOnInterval_AndRejectsNonPositive()
    {
        var monster = new Monster("Beast", 1, maxHp: 30, attackPower: 5, defense: 2, speed: 9, xpReward: 40, maxIons: 20);
        monster.Ions.Spend(monster.Ions.Current);

        Assert.False(monster.AdvanceIonRegenTick(3));
        Assert.False(monster.AdvanceIonRegenTick(3));
        Assert.True(monster.AdvanceIonRegenTick(3));
        Assert.Equal(1, monster.Ions.Current);

        Assert.Throws<ArgumentOutOfRangeException>(() => monster.AdvanceIonRegenTick(0));
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
