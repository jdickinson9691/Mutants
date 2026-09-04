using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Monsters;
using ChronoTravelers.Core.Traits;
using ChronoTravelers.Core.World;

namespace ChronoTravelers.Core.Tests.Monsters;

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
        Assert.Equal(MonsterScaling.BaseTachyons(2), monster.Tachyons.Max);
        Assert.Equal(monster.Tachyons.Max, monster.Tachyons.Current);
    }

    [Fact]
    public void Constructor_ExplicitMaxTachyonsOverridesTheScaledDefault()
    {
        var monster = new Monster("Beast", 3, 40, 5, 3, 10, 60, maxTachyons: 99);
        Assert.Equal(99, monster.Tachyons.Max);
    }

    [Fact]
    public void PendingDefensePenalty_DefaultsToZero()
    {
        Assert.Equal(0, Monster.Create("Beast", 1).PendingDefensePenalty);
    }

    [Fact]
    public void EquipWeapon_AddsItsAttackBonusToEffectiveAttackPower_AndDropsWithInventory()
    {
        var monster = new Monster("Brute", 1, 30, 10, 2, 8, 40);
        Assert.Null(monster.EquippedWeapon);
        Assert.Equal(10, monster.EffectiveAttackPower);

        var blade = Item.Create("Scav Blade", ItemType.Weapon, 4, Rarity.Rare);
        monster.EquipWeapon(blade);

        Assert.Same(blade, monster.EquippedWeapon);
        Assert.Equal(10 + blade.AttackBonus, monster.EffectiveAttackPower);
        Assert.Contains(blade, monster.Inventory); // so it drops with everything else on death
    }

    [Fact]
    public void EquipWeapon_RejectsANonWeapon()
    {
        var monster = new Monster("Brute", 1, 30, 10, 2, 8, 40);
        Assert.Throws<ArgumentException>(() => monster.EquipWeapon(Item.Create("Ration", ItemType.Consumable, 1, Rarity.Common)));
    }

    [Fact]
    public void ConvertingTheEquippedWeapon_ClearsTheEquipSlot()
    {
        var monster = new Monster("Brute", 1, 30, 10, 2, 8, 40, maxTachyons: 50);
        monster.Tachyons.Spend(monster.Tachyons.Current);
        var blade = Item.Create("Scav Blade", ItemType.Weapon, 4, Rarity.Rare);
        monster.EquipWeapon(blade);

        monster.Convert(blade);

        Assert.Null(monster.EquippedWeapon);
        Assert.Equal(10, monster.EffectiveAttackPower);
    }

    [Fact]
    public void IsApex_DefaultsFalse_AndCanBeSet()
    {
        Assert.False(Monster.Create("Beast", 1).IsApex);
        Assert.True(Monster.Create("Frayed Beast", 1, isApex: true).IsApex);
        Assert.True(new Monster("Frayed Beast", 1, 80, 10, 4, 9, 200, isApex: true).IsApex);
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
    public void Heal_SpendsTachyonsAtTheHealRatioCappedByMissingHpAndAvailableTachyons()
    {
        var monster = new Monster("Beast", 2, maxHp: 40, attackPower: 5, defense: 3, speed: 10, xpReward: 80, maxTachyons: 20);
        monster.Health.Damage(9); // exactly 3 Tachyons' worth at 3:1

        var healed = monster.Heal();

        Assert.Equal(9, healed);
        Assert.Equal(monster.Health.Max, monster.Health.Current);
        Assert.Equal(17, monster.Tachyons.Current); // spent 3

        // Now drain Tachyons and confirm heal no-ops without spending.
        monster.Health.Damage(20);
        monster.Tachyons.Spend(monster.Tachyons.Current);
        Assert.Equal(0, monster.Heal());
    }

    [Fact]
    public void Convert_DestroysACarriedItemForTachyons()
    {
        var monster = new Monster("Beast", 2, maxHp: 40, attackPower: 5, defense: 3, speed: 10, xpReward: 80, maxTachyons: 200);
        monster.Tachyons.Spend(monster.Tachyons.Current); // headroom to observe the gain
        var item = Item.Create("Neon Shard", ItemType.Junk, 3, Rarity.Rare);
        monster.AddToInventory(item);

        var gained = monster.Convert(item);

        Assert.True(gained > 0);
        Assert.Equal(gained, monster.Tachyons.Current);
        Assert.Empty(monster.Inventory);
        Assert.Throws<InvalidOperationException>(() => monster.Convert(item));
    }

    [Fact]
    public void AdvanceTachyonRegenTick_AddsOneTachyonOnInterval_AndRejectsNonPositive()
    {
        var monster = new Monster("Beast", 1, maxHp: 30, attackPower: 5, defense: 2, speed: 9, xpReward: 40, maxTachyons: 20);
        monster.Tachyons.Spend(monster.Tachyons.Current);

        Assert.False(monster.AdvanceTachyonRegenTick(3));
        Assert.False(monster.AdvanceTachyonRegenTick(3));
        Assert.True(monster.AdvanceTachyonRegenTick(3));
        Assert.Equal(1, monster.Tachyons.Current);

        Assert.Throws<ArgumentOutOfRangeException>(() => monster.AdvanceTachyonRegenTick(0));
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

    [Fact]
    public void Name_StartsAsJustTheBaseNameUntilEnumerated()
    {
        var monster = Monster.Create("Ashfall Echo", tier: 1);

        Assert.Equal("Ashfall Echo", monster.Name);
        Assert.Equal("Ashfall Echo", monster.BaseName);
    }

    [Fact]
    public void Enumerate_AppendsAThreeDigitZeroPaddedSuffix()
    {
        var monster = Monster.Create("Ashfall Echo", tier: 1);

        monster.Enumerate(42);

        Assert.Equal("Ashfall Echo-042", monster.Name);
        Assert.Equal("Ashfall Echo", monster.BaseName); // BaseName never changes
    }

    [Fact]
    public void Enumerate_WrapsAnOutOfRangeNumberIntoThreeDigits()
    {
        var monster = Monster.Create("Beast", tier: 1);

        monster.Enumerate(1042); // > 999

        Assert.Equal("Beast-042", monster.Name);
    }

    [Fact]
    public void Enumerate_IsANoOpOnceAlreadyEnumerated()
    {
        var monster = Monster.Create("Beast", tier: 1);

        monster.Enumerate(7);
        monster.Enumerate(999); // should not overwrite the first callsign

        Assert.Equal("Beast-007", monster.Name);
    }

    // --- CreatureTraitKind ---------------------------------------------

    [Fact]
    public void Trait_DefaultsToNoneUntilAssigned()
    {
        var monster = Monster.Create("Beast", 1);
        Assert.Equal(CreatureTraitKind.None, monster.Trait);
    }

    [Fact]
    public void AssignTrait_SetsTheTrait()
    {
        var monster = Monster.Create("Beast", 1);

        monster.AssignTrait(CreatureTraitKind.Aggressive);

        Assert.Equal(CreatureTraitKind.Aggressive, monster.Trait);
    }

    [Fact]
    public void AssignTrait_IsANoOpOnceAlreadyAssigned_EvenIfTheFirstRollWasNone()
    {
        var monster = Monster.Create("Beast", 1);

        monster.AssignTrait(CreatureTraitKind.None); // a legitimate "missed the roll" result
        monster.AssignTrait(CreatureTraitKind.Hoarder); // should not silently overwrite it

        Assert.Equal(CreatureTraitKind.None, monster.Trait);
    }

    [Fact]
    public void Convert_WithScavengerTrait_GainsABonusOverAPlainMonster()
    {
        // Explicit maxTachyons headroom so neither conversion caps out and
        // masks the bonus (Monster.Create's tier-1 pool is far smaller) —
        // but the pool still starts full (TachyonPool's constructor
        // defaults Current to Max), so it has to be drained back to 0
        // first or Convert's Add has no room to show a difference at all.
        var plain = new Monster("Beast", 1, 30, 10, 2, 8, 40, maxTachyons: 10000);
        var scavenger = new Monster("Scav Beast", 1, 30, 10, 2, 8, 40, maxTachyons: 10000);
        scavenger.AssignTrait(CreatureTraitKind.Scavenger);
        plain.Tachyons.Spend(plain.Tachyons.Current);
        scavenger.Tachyons.Spend(scavenger.Tachyons.Current);

        var item1 = Item.Create("Neon Shard", ItemType.Junk, 3, Rarity.Rare);
        var item2 = Item.Create("Neon Shard", ItemType.Junk, 3, Rarity.Rare);
        plain.AddToInventory(item1);
        scavenger.AddToInventory(item2);

        var plainGain = plain.Convert(item1);
        var scavengerGain = scavenger.Convert(item2);

        Assert.True(scavengerGain > plainGain);
    }
}
