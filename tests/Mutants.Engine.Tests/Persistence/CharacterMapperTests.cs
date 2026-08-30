using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.Items;
using Mutants.Engine.Persistence;

namespace Mutants.Engine.Tests.Persistence;

public class CharacterMapperTests
{
    [Fact]
    public void RoundTrip_PreservesCoreStats()
    {
        var original = new Mutant("Rook", CharacterClass.Warrior);
        original.GainXp(150);
        original.AddRiblets(42);
        original.UnlockTimeLevel(2);
        original.SetCurrentTimeLevel(2);
        original.RecordGatekeeperDefeat(2);
        original.PlaceAt(new Core.World.Coordinate(3, -2));

        var restored = CharacterMapper.FromSaveData(CharacterMapper.ToSaveData(original));

        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.Class, restored.Class);
        Assert.Equal(original.Level, restored.Level);
        Assert.Equal(original.Xp, restored.Xp);
        Assert.Equal(original.Stats, restored.Stats);
        Assert.Equal(original.Health.Current, restored.Health.Current);
        Assert.Equal(original.Health.Max, restored.Health.Max);
        Assert.Equal(original.Ions.Current, restored.Ions.Current);
        Assert.Equal(original.Ions.Max, restored.Ions.Max);
        Assert.Equal(original.Riblets, restored.Riblets);
        Assert.Equal(original.UnlockedTimeLevel, restored.UnlockedTimeLevel);
        Assert.Equal(original.CurrentTimeLevel, restored.CurrentTimeLevel);
        Assert.Equal(original.Position, restored.Position);
        Assert.True(restored.HasDefeatedGatekeeper(2));
    }

    [Fact]
    public void RoundTrip_PreservesInventoryAndEquippedItems()
    {
        var original = new Mutant("Rook", CharacterClass.Warrior);
        var weapon = Item.Create("Axe", ItemType.Weapon, 2, Rarity.Rare, CharacterClass.Warrior);
        var armor = Item.Create("Plate", ItemType.Armor, 2, Rarity.Uncommon);
        var junk = Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common);
        original.AddToInventory(weapon);
        original.AddToInventory(armor);
        original.AddToInventory(junk);
        original.Wield(weapon);
        original.Wield(armor);

        var restored = CharacterMapper.FromSaveData(CharacterMapper.ToSaveData(original));

        Assert.Equal(3, restored.Inventory.Count);
        Assert.Contains(restored.Inventory, i => i == weapon);
        Assert.Contains(restored.Inventory, i => i == armor);
        Assert.Contains(restored.Inventory, i => i == junk);
        Assert.Equal(weapon, restored.EquippedWeapon);
        Assert.Equal(armor, restored.EquippedArmor);
    }

    [Fact]
    public void RoundTrip_HandlesNoEquippedItems()
    {
        var original = new Mutant("Rook", CharacterClass.Warrior);
        original.AddToInventory(Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common));

        var restored = CharacterMapper.FromSaveData(CharacterMapper.ToSaveData(original));

        Assert.Null(restored.EquippedWeapon);
        Assert.Null(restored.EquippedArmor);
    }

    [Fact]
    public void RoundTrip_HandlesEmptyInventory()
    {
        var original = new Mutant("Rook", CharacterClass.Warrior);
        var restored = CharacterMapper.FromSaveData(CharacterMapper.ToSaveData(original));

        Assert.Empty(restored.Inventory);
    }
}
