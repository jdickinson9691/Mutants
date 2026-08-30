using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.Items;
using Mutants.Core.Stats;
using Mutants.Core.World;

namespace Mutants.Core.Tests.Characters;

public class MutantTests
{
    [Fact]
    public void Constructor_StartsAtLevelOneWithClassBaseStatsAndFullPools()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);

        Assert.Equal(1, mutant.Level);
        Assert.Equal(0, mutant.Xp);
        Assert.Equal(ClassDefinition.For(CharacterClass.Warrior).BaseStats, mutant.Stats);
        Assert.Equal(mutant.Health.Max, mutant.Health.Current);
        Assert.Equal(mutant.Ions.Max, mutant.Ions.Current);
    }

    [Fact]
    public void Constructor_RejectsEmptyName()
    {
        Assert.Throws<ArgumentException>(() => new Mutant("", CharacterClass.Warrior));
    }

    [Fact]
    public void LevelUp_IncreasesPrimaryStatAndMaxPools()
    {
        var mutant = new Mutant("Rook", CharacterClass.Mage);
        var startingIntellect = mutant.Stats.Intellect;
        var startingMaxHp = mutant.Health.Max;
        var startingMaxIons = mutant.Ions.Max;

        mutant.LevelUp();

        Assert.Equal(2, mutant.Level);
        Assert.Equal(startingIntellect + 1, mutant.Stats.Intellect);
        Assert.True(mutant.Health.Max > startingMaxHp);
        Assert.True(mutant.Ions.Max > startingMaxIons);
    }

    [Fact]
    public void GainXp_AppliesMultipleLevelUpsWhenEnoughXpIsAwardedAtOnce()
    {
        var mutant = new Mutant("Rook", CharacterClass.Thief);
        var xpForLevel3 = Leveling.CumulativeXpForLevel(3);

        var levelsGained = mutant.GainXp(xpForLevel3);

        Assert.Equal(2, levelsGained);
        Assert.Equal(3, mutant.Level);
    }

    [Fact]
    public void GainXp_StopsAtSoftLevelCapForUnlockedTimeLevel()
    {
        // unlockedTimeLevel 1 -> soft cap of character level 10.
        var mutant = new Mutant("Rook", CharacterClass.Priest, unlockedTimeLevel: 1);

        mutant.GainXp(Leveling.CumulativeXpForLevel(Leveling.MaxCharacterLevel));

        Assert.Equal(10, mutant.Level);
    }

    [Fact]
    public void UnlockTimeLevel_RaisesSoftCapAndNeverRegresses()
    {
        var mutant = new Mutant("Rook", CharacterClass.Priest, unlockedTimeLevel: 1);

        mutant.UnlockTimeLevel(3);
        Assert.Equal(3, mutant.UnlockedTimeLevel);

        mutant.UnlockTimeLevel(2); // lower than current — should be ignored
        Assert.Equal(3, mutant.UnlockedTimeLevel);
    }

    [Fact]
    public void Convert_RemovesItemFromInventoryAndAddsIons()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        mutant.Ions.Spend(mutant.Ions.Current); // drain to 0 so Add() has headroom to observe
        var item = Item.Create("Scrap Metal", ItemType.Junk, tier: 1, Rarity.Common); // value 10
        mutant.AddToInventory(item);

        var gained = mutant.Convert(item);

        Assert.Equal(4, gained); // floor(10 * 0.4) = 4
        Assert.DoesNotContain(item, mutant.Inventory);
        Assert.Equal(4, mutant.Ions.Current);
    }

    [Fact]
    public void Convert_ThrowsIfItemNotInInventory()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var item = Item.Create("Ghost Item", ItemType.Junk, 1, Rarity.Common);

        Assert.Throws<InvalidOperationException>(() => mutant.Convert(item));
    }

    [Fact]
    public void Wield_EquipsWeaponAndArmorIntoSeparateSlots()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var weapon = Item.Create("Axe", ItemType.Weapon, 1, Rarity.Common);
        var armor = Item.Create("Plate", ItemType.Armor, 1, Rarity.Common);
        mutant.AddToInventory(weapon);
        mutant.AddToInventory(armor);

        mutant.Wield(weapon);
        mutant.Wield(armor);

        Assert.Equal(weapon, mutant.EquippedWeapon);
        Assert.Equal(armor, mutant.EquippedArmor);
    }

    [Fact]
    public void Wield_ThrowsForNonWieldableItem()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var potion = Item.Create("Elixir", ItemType.Consumable, 1, Rarity.Common);
        mutant.AddToInventory(potion);

        Assert.Throws<InvalidOperationException>(() => mutant.Wield(potion));
    }

    [Fact]
    public void Wield_AllowsOffClassGearAtAPenalty_RatherThanBlockingIt()
    {
        var mage = new Mutant("Zeta", CharacterClass.Mage);
        var warriorAxe = Item.Create("Great Axe", ItemType.Weapon, 1, Rarity.Common, CharacterClass.Warrior);
        mage.AddToInventory(warriorAxe);

        mage.Wield(warriorAxe); // must not throw — GDD §4.3 penalty, not hard block

        Assert.Equal(warriorAxe, mage.EquippedWeapon);
        Assert.True(warriorAxe.WieldEffectiveness(CharacterClass.Mage) < 1.0);
    }

    [Fact]
    public void Wield_ThrowsIfItemNotInInventory()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var weapon = Item.Create("Axe", ItemType.Weapon, 1, Rarity.Common);

        Assert.Throws<InvalidOperationException>(() => mutant.Wield(weapon));
    }

    [Fact]
    public void Position_DefaultsToOrigin()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        Assert.Equal(Coordinate.Origin, mutant.Position);
    }

    [Fact]
    public void PlaceAt_AndMoveTo_UpdatePosition()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var start = new Coordinate(3, 1);
        mutant.PlaceAt(start);
        Assert.Equal(start, mutant.Position);

        var next = start.Move(Direction.North);
        mutant.MoveTo(next);
        Assert.Equal(next, mutant.Position);
    }

    [Fact]
    public void Riblets_AddAndSpendTrackBalance()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        Assert.Equal(0, mutant.Riblets);

        mutant.AddRiblets(50);
        Assert.Equal(50, mutant.Riblets);

        mutant.SpendRiblets(20);
        Assert.Equal(30, mutant.Riblets);
    }

    [Fact]
    public void SpendRiblets_ThrowsWhenUnaffordable()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        mutant.AddRiblets(10);

        Assert.Throws<InvalidOperationException>(() => mutant.SpendRiblets(11));
    }
}
