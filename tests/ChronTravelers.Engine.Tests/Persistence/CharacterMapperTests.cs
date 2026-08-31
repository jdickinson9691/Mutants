using ChronTravelers.Core.Characters;
using ChronTravelers.Core.Classes;
using ChronTravelers.Core.Economy;
using ChronTravelers.Core.Items;
using ChronTravelers.Core.Time;
using ChronTravelers.Engine.Persistence;

namespace ChronTravelers.Engine.Tests.Persistence;

public class CharacterMapperTests
{
    private const long TestSeed = 8675309L;

    [Fact]
    public void RoundTrip_PreservesCoreStatsAndTimelinePosition()
    {
        var original = new Traveler("Rook", CharacterClass.Soldier);
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
            Class = nameof(CharacterClass.Spy),
            Level = 22,
            Xp = 5000,
            Strength = 12,
            Agility = 30,
            Resolve = 10,
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
        var original = new Traveler("Rook", CharacterClass.Soldier);
        var weapon = Item.Create("Axe", ItemType.Weapon, 2, Rarity.Rare, CharacterClass.Soldier);
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
        var original = new Traveler("Rook", CharacterClass.Soldier);
        original.AddToInventory(Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common));

        var restored = CharacterMapper.FromSaveData(CharacterMapper.ToSaveData(original, TestSeed));

        Assert.Null(restored.EquippedWeapon);
        Assert.Null(restored.EquippedArmor);
    }

    [Fact]
    public void RoundTrip_HandlesEmptyInventory()
    {
        var original = new Traveler("Rook", CharacterClass.Soldier);
        var restored = CharacterMapper.FromSaveData(CharacterMapper.ToSaveData(original, TestSeed));

        Assert.Empty(restored.Inventory);
    }

    [Fact]
    public void RoundTrip_PreservesAConsumablesEffectFields()
    {
        var original = new Traveler("Rook", CharacterClass.Soldier);
        var potion = Item.Create("Combat Stim", ItemType.Consumable, 3, Rarity.Uncommon,
            consumableEffect: ConsumableEffectType.BuffAttack, effectMagnitude: 5, effectDurationTicks: 15);
        original.AddToInventory(potion);

        var restored = CharacterMapper.FromSaveData(CharacterMapper.ToSaveData(original, TestSeed));

        var restoredPotion = Assert.Single(restored.Inventory);
        Assert.Equal(ConsumableEffectType.BuffAttack, restoredPotion.ConsumableEffect);
        Assert.Equal(5, restoredPotion.EffectMagnitude);
        Assert.Equal(15, restoredPotion.EffectDurationTicks);
        Assert.True(restoredPotion.IsUsable);
    }

    [Fact]
    public void RoundTrip_PreservesAHalfSpentEquippedRangedWeapon_AndReEquipsIt()
    {
        var original = new Traveler("Rook", CharacterClass.Soldier);
        var wand = Item.CreateRanged("Hexbolt Wand", 3, Rarity.Rare, RangedKind.Wand, ammoCapacity: 5,
            rangedEffect: RangedEffectType.Weaken, magnitude: 2);
        wand.AmmoRemaining = 2; // fired three of five
        var junk = Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common);
        original.AddToInventory(wand);
        original.AddToInventory(junk);
        original.Wield(wand);

        var restored = CharacterMapper.FromSaveData(CharacterMapper.ToSaveData(original, TestSeed));

        var restoredWand = restored.Inventory.Single(i => i.IsRanged);
        Assert.Equal(RangedKind.Wand, restoredWand.RangedKind);
        Assert.Equal(RangedEffectType.Weaken, restoredWand.RangedEffect);
        Assert.Equal(5, restoredWand.AmmoCapacity);
        Assert.Equal(2, restoredWand.AmmoRemaining);
        Assert.Equal(wand.InstanceId, restoredWand.InstanceId);
        Assert.Equal(restoredWand, restored.EquippedRanged);
    }

    [Fact]
    public void FromSaveData_LeavesEquippedRangedNull_ForALegacyBlobWithoutTheField()
    {
        var restored = CharacterMapper.FromSaveData(new CharacterSaveData
        {
            SchemaVersion = 2,
            Name = "Rook",
            Class = nameof(CharacterClass.Soldier),
            Strength = 10, Agility = 10, Resolve = 10, Intellect = 10,
            CurrentHp = 30, MaxHp = 30, CurrentIons = 10, MaxIons = 10,
            CurrentYear = 2000, FurthestYearReached = 2000,
        });

        Assert.Null(restored.EquippedRanged);
    }

    [Fact]
    public void OwnedStores_RoundTripAcrossSave_ThenApplyOwnedStores_RestoresOwnershipCapitalAndListings()
    {
        var world = TestTimeWorld.Build(seed: 4242);
        var year = 2600;

        var player = new Traveler("Rook", CharacterClass.Soldier, startingYear: year);
        player.AddRiblets(5000);
        player.PlaceAt(world.GetYear(year).Map.Start);

        var slot = world.GetYear(year).StoreSlots.Single(s => s.IsAvailableForPurchase);
        var store = slot.Purchase(player, startingCapital: 100);
        var ribletsAfterPurchase = player.Riblets;

        var listedItem = Item.Create("Layered Plating", ItemType.Armor, 5, Rarity.Rare);
        player.AddToInventory(listedItem);
        store.Deposit(player, listedItem, askingPrice: 250);

        var save = CharacterMapper.ToSaveData(player, TestSeed,
            new Dictionary<int, Store> { [year] = store });

        Assert.Single(save.OwnedStores);
        Assert.Equal(year, save.OwnedStores[0].Year);
        Assert.Equal(100, save.OwnedStores[0].Capital);
        Assert.Single(save.OwnedStores[0].Listings);

        // Fresh session: rebuild the world from the same seed, restore the character, re-attach stores.
        var reloadedWorld = TestTimeWorld.Build(seed: 4242);
        var reloaded = CharacterMapper.FromSaveData(save);
        var ribletsOnReload = reloaded.Riblets;
        CharacterMapper.ApplyOwnedStores(save, reloaded, reloadedWorld);

        var reloadedSlot = reloadedWorld.GetYear(year).StoreSlots.Single(s => s.Store?.Owner == reloaded);
        Assert.NotNull(reloadedSlot.Store);
        Assert.Equal(100, reloadedSlot.Store!.Capital);
        var reloadedListing = Assert.Single(reloadedSlot.Store.Listings);
        Assert.Equal("Layered Plating", reloadedListing.Item.Name);
        Assert.Equal(250, reloadedListing.AskingPrice);

        // Re-attaching does not charge the purchase cost again.
        Assert.Equal(ribletsOnReload, reloaded.Riblets);
        Assert.Equal(ribletsAfterPurchase, ribletsOnReload);
    }

    [Fact]
    public void ApplyOwnedStores_IsANoOpForASaveWithNoOwnedStores()
    {
        var world = TestTimeWorld.Build(seed: 1);
        var save = CharacterMapper.ToSaveData(new Traveler("Rook", CharacterClass.Soldier), TestSeed);
        var player = CharacterMapper.FromSaveData(save);

        CharacterMapper.ApplyOwnedStores(save, player, world); // must not throw

        Assert.All(world.GetYear(2000).StoreSlots, s => Assert.NotEqual(player, s.Store?.Owner));
    }
}
