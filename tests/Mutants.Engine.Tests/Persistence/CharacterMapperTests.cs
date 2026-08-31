using Mutants.Core.Characters;
using Mutants.Core.Classes;
using Mutants.Core.Items;
using Mutants.Engine.Persistence;

namespace Mutants.Engine.Tests.Persistence;

public class CharacterMapperTests
{
    private const long TestSeed = 8675309L;

    [Fact]
    public void RoundTrip_PreservesCoreStatsAndTimelinePosition()
    {
        var original = new Mutant("Rook", CharacterClass.Warrior);
        original.GainXp(150);
        original.AddRiblets(42);
        original.SetCurrentYear(3400);
        original.SetCurrentYear(2600); // current moves back; furthest stays at 3400
        original.RecordGatekeeperDefeat(3187);
        original.PlaceAt(new Core.World.Coordinate(3, -2));

        var save = CharacterMapper.ToSaveData(original, TestSeed);
        var restored = CharacterMapper.FromSaveData(save);

        Assert.Equal(CharacterSaveData.CurrentSchemaVersion, save.SchemaVersion);
        Assert.Equal(TestSeed, save.WorldSeed);
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
        Assert.Equal(2600, restored.CurrentYear);
        Assert.Equal(3400, restored.FurthestYearReached);
        Assert.Equal(original.Position, restored.Position);
        Assert.True(restored.HasDefeatedGatekeeper(3187));
    }

    [Fact]
    public void FromSaveData_MigratesASchemaOneBlob_ToTheTimeline()
    {
        var legacy = new CharacterSaveData
        {
            SchemaVersion = 1,
            Name = "Legacy",
            Class = nameof(CharacterClass.Thief),
            Level = 22,
            Xp = 5000,
            Strength = 12,
            Agility = 30,
            Faith = 10,
            Intellect = 14,
            CurrentHp = 40,
            MaxHp = 80,
            CurrentIons = 15,
            MaxIons = 60,
            Riblets = 700,
            UnlockedTimeLevel = 5,
            CurrentTimeLevel = 4,
            DefeatedGatekeepers = [2, 3, 4], // old level numbers — discarded by the migration
        };

        var restored = CharacterMapper.FromSaveData(legacy);

        Assert.Equal("Legacy", restored.Name);
        Assert.Equal(22, restored.Level);
        Assert.Equal(700, restored.Riblets);
        // old level 4 -> 2000 + 3*375 = 3125; furthest from old level 5 -> 3500.
        Assert.Equal(3125, restored.CurrentYear);
        Assert.Equal(3500, restored.FurthestYearReached);
        Assert.Empty(restored.DefeatedGatekeeperYears);
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

        var restored = CharacterMapper.FromSaveData(CharacterMapper.ToSaveData(original, TestSeed));

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

        var restored = CharacterMapper.FromSaveData(CharacterMapper.ToSaveData(original, TestSeed));

        Assert.Null(restored.EquippedWeapon);
        Assert.Null(restored.EquippedArmor);
    }

    [Fact]
    public void RoundTrip_HandlesEmptyInventory()
    {
        var original = new Mutant("Rook", CharacterClass.Warrior);
        var restored = CharacterMapper.FromSaveData(CharacterMapper.ToSaveData(original, TestSeed));

        Assert.Empty(restored.Inventory);
    }
}
