using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Economy;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Monsters;
using ChronoTravelers.Core.Time;
using ChronoTravelers.Core.World;
using ChronoTravelers.Engine.Content;
using ChronoTravelers.Engine.Npc;

namespace ChronoTravelers.Engine.Tests.Npc;

public class NpcControllerTests
{
    private static readonly LevelMap TestLevelMap = TestLevel.Build();

    private static Traveler FreshNpc(CharacterClass characterClass = CharacterClass.Soldier, int year = 2000)
    {
        var npc = new Traveler("Vex", characterClass, startingYear: year);
        npc.PlaceAt(Coordinate.Origin);
        return npc;
    }

    /// <summary>Wraps an already-occupied <see cref="Store"/> in a <see cref="StoreSlot"/> — most of these tests only care about trading at a store that's already there, not slot purchase/vacancy mechanics.</summary>
    private static StoreSlot OccupiedSlot(Store store) =>
        new(store.Name, Coordinate.Origin, homeLevel: store.HomeLevel, purchaseCost: 0, store);

    /// <summary>A Soldier levelled far enough (and topped off) that its Tachyon pool can afford a year-hop.</summary>
    private static Traveler ReadyToTravelNpc(int year = 2000)
    {
        var npc = FreshNpc(year: year);
        for (var i = 1; i < 20; i++)
        {
            npc.LevelUp();
        }

        npc.Health.Heal(npc.Health.Max);
        npc.Tachyons.SetMax(500);
        npc.Tachyons.Add(500);
        return npc;
    }

    [Fact]
    public void Act_DeadNpc_ReturnsIdleAndDoesNothing()
    {
        var npc = FreshNpc();
        npc.Health.Damage(npc.Health.Max);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5));

        Assert.Equal(NpcGoal.Idle, result.Goal);
        Assert.Equal(Coordinate.Origin, npc.Position);
    }

    [Fact]
    public void Act_LowTachyonsWithFodder_ConvertsAndSeeksTachyons()
    {
        var npc = FreshNpc();
        npc.Tachyons.Spend(npc.Tachyons.Current);
        var junk = Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common);
        npc.AddToInventory(junk);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5));

        Assert.Equal(NpcGoal.SeekTachyons, result.Goal);
        Assert.DoesNotContain(junk, npc.Inventory);
        Assert.True(npc.Tachyons.Current > 0);
    }

    [Fact]
    public void Act_LowTachyonsWithNoFodder_FallsThroughToGrind()
    {
        var npc = FreshNpc();
        npc.Tachyons.Spend(npc.Tachyons.Current);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5));

        Assert.Equal(NpcGoal.Grind, result.Goal);
    }

    [Fact]
    public void Act_LowHealth_Retreats_AndDoesNotFight()
    {
        var npc = FreshNpc();
        npc.Health.Damage(npc.Health.Max - 1);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5));

        Assert.Equal(NpcGoal.Retreat, result.Goal);
        Assert.Null(result.Fight);
    }

    [Fact]
    public void Act_Default_WandersAndFightsAMonster()
    {
        var npc = FreshNpc();

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5));

        Assert.Equal(NpcGoal.Grind, result.Goal);
        Assert.NotNull(result.MonsterName);
        Assert.NotNull(result.Fight);
        Assert.NotEqual(Coordinate.Origin, npc.Position);
    }

    [Fact]
    public void Act_ExcessJunkWithAStoreAvailable_SellsOneItem()
    {
        var npc = FreshNpc();
        for (var tier = 1; tier <= 4; tier++)
        {
            npc.AddToInventory(Item.Create($"Junk Tier {tier}", ItemType.Junk, tier, Rarity.Common));
        }

        var store = Store.CreateGovernmentStore("Test Store", homeLevel: 1);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5), [OccupiedSlot(store)]);

        Assert.Equal(NpcGoal.Trade, result.Goal);
        Assert.Equal(3, npc.Inventory.Count(i => i.Type == ItemType.Junk));
        Assert.True(npc.Credits > 0);
        Assert.Single(store.Listings);
    }

    [Fact]
    public void Act_UnarmedWithCredits_BuysAndWieldsAnAffordableWeapon()
    {
        var npc = FreshNpc();
        npc.AddCredits(100);
        var store = Store.CreateGovernmentStore("Test Store", homeLevel: 1);
        var weapon = Item.Create("Cracked Shiv", ItemType.Weapon, 1, Rarity.Common);
        store.Stock(weapon, askingPrice: 50);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5), [OccupiedSlot(store)]);

        Assert.Equal(NpcGoal.Trade, result.Goal);
        Assert.Equal(weapon, npc.EquippedWeapon);
        Assert.Equal(50, npc.Credits);
    }

    /// <summary>
    /// Regression test for a real crash (crash-20260903-142248-724.log,
    /// crash-20260904-081415-156.log): Store.SellToTraveler silently
    /// refuses (no charge, nothing added) when the buyer's pack is
    /// already full, but TryTrade's "buy a weapon" branch used to call
    /// Wield unconditionally afterward, throwing InvalidOperationException
    /// for an item that was never actually added to the NPC's inventory
    /// and taking the whole process down. Fill the pack with non-junk,
    /// non-wieldable filler so neither of TryTrade's earlier branches
    /// (sell surplus gear / sell excess junk) fires and frees up space
    /// before the buy-weapon branch is reached.
    /// </summary>
    [Fact]
    public void Act_UnarmedWithCreditsButAFullPack_DoesNotCrash_AndDoesNotWieldAnUnpurchasedWeapon()
    {
        var npc = FreshNpc();
        npc.AddCredits(100);
        for (var i = 0; i < Traveler.MaxInventorySize; i++)
        {
            npc.AddToInventory(Item.Create($"Ration {i}", ItemType.Consumable, 1, Rarity.Common));
        }

        var store = Store.CreateGovernmentStore("Test Store", homeLevel: 1);
        var weapon = Item.Create("Rusted Shiv", ItemType.Weapon, 1, Rarity.Common);
        store.Stock(weapon, askingPrice: 50);

        var exception = Record.Exception(() =>
            NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5), [OccupiedSlot(store)]));

        Assert.Null(exception);
        Assert.Null(npc.EquippedWeapon);
        Assert.Equal(100, npc.Credits); // the sale never charged anything
        Assert.Single(store.Listings); // and never left the store either
    }

    [Fact]
    public void Act_ArmedWithCredits_BuysAListedUpgradeAndDrainsTheListing()
    {
        var npc = FreshNpc();
        var worn = new Item("Bent Pipe", ItemType.Weapon, 1, Rarity.Common, Value: 10, AttackBonus: 3);
        npc.AddToInventory(worn);
        npc.Wield(worn);
        npc.AddCredits(200);

        var store = Store.CreateGovernmentStore("Test Store", homeLevel: 1);
        var upgrade = new Item("Riot Baton", ItemType.Weapon, 1, Rarity.Uncommon, Value: 40, AttackBonus: 12);
        store.Stock(upgrade, askingPrice: 40);

        // 0.1 clears the ShopPurchaseChance gate (0.3).
        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.1), [OccupiedSlot(store)]);

        Assert.Equal(NpcGoal.Trade, result.Goal);
        Assert.Equal(upgrade, npc.EquippedWeapon);
        Assert.Empty(store.Listings);
        Assert.Equal(160, npc.Credits);
    }

    [Fact]
    public void Act_ArmedWithCredits_DoesNotBuyAListingThatIsntAnUpgrade()
    {
        var npc = FreshNpc();
        var worn = new Item("Good Blade", ItemType.Weapon, 1, Rarity.Rare, Value: 60, AttackBonus: 20);
        npc.AddToInventory(worn);
        npc.Wield(worn);
        npc.AddCredits(200);

        var store = Store.CreateGovernmentStore("Test Store", homeLevel: 1);
        store.Stock(new Item("Bent Pipe", ItemType.Weapon, 1, Rarity.Common, Value: 10, AttackBonus: 3), askingPrice: 10);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.1), [OccupiedSlot(store)]);

        Assert.Equal(NpcGoal.Grind, result.Goal);
        Assert.Equal(worn, npc.EquippedWeapon);
        Assert.Single(store.Listings);
    }

    [Fact]
    public void Act_StoresAvailableButNothingToTrade_StillGrinds()
    {
        var npc = FreshNpc();
        var store = Store.CreateGovernmentStore("Test Store", homeLevel: 1);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5), [OccupiedSlot(store)]);

        Assert.Equal(NpcGoal.Grind, result.Goal);
    }

    [Fact]
    public void Act_NoWorldGiven_NeverAttemptsTravel()
    {
        var npc = ReadyToTravelNpc();

        // 0.01 would pass the travel-chance gate if a world were given.
        var result = NpcController.Act(npc, TestLevelMap, new StubRandomSource(0.01, 0.5));

        Assert.NotEqual(NpcGoal.Travel, result.Goal);
        Assert.Equal(2000, npc.CurrentYear);
    }

    [Fact]
    public void Act_TravelChanceNotRolled_FallsThroughToGrind()
    {
        var npc = ReadyToTravelNpc();
        var world = TestTimeWorld.Build();

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5), world: world); // 0.5 > the 0.10 gate

        Assert.Equal(NpcGoal.Grind, result.Goal);
        Assert.Equal(2000, npc.CurrentYear);
    }

    [Fact]
    public void Act_TravelChanceRolled_JumpsForwardAlongTheTimeline()
    {
        var npc = ReadyToTravelNpc();
        var world = TestTimeWorld.Build();

        // 0.01 passes the gate; 0.5 -> a mid-range hop; 0.5 -> forward (< 0.8 bias).
        var result = NpcController.Act(npc, TestLevelMap, new StubRandomSource(0.01, 0.5, 0.5, 0.5), world: world);

        Assert.Equal(NpcGoal.Travel, result.Goal);
        Assert.True(npc.CurrentYear > 2000);
        Assert.InRange(npc.CurrentYear, 2001, TimeScale.MaxYear);
    }

    [Fact]
    public void Act_TravelRolledButCannotAffordTheTachyonCost_FallsThroughToGrind()
    {
        var npc = FreshNpc(); // level 1 Soldier — ~20 max Tachyons
        npc.Tachyons.Spend(npc.Tachyons.Current); // 0 Tachyons — can't afford any hop
        var world = TestTimeWorld.Build();

        var result = NpcController.Act(npc, TestLevelMap, new StubRandomSource(0.01, 0.5, 0.5, 0.5), world: world);

        Assert.NotEqual(NpcGoal.Travel, result.Goal);
        Assert.Equal(2000, npc.CurrentYear);
    }

    [Fact]
    public void Act_TravelRolledAtTheEndOfTheTimeline_StaysPutAndGrinds()
    {
        var npc = ReadyToTravelNpc(year: TimeScale.MaxYear);
        var world = TestTimeWorld.Build();

        // Gate passes, hop forward — but there's nowhere forward from 5000, so it clamps to the same year and bails.
        var result = NpcController.Act(npc, TestLevelMap, new StubRandomSource(0.01, 0.5, 0.5, 0.5), world: world);

        Assert.Equal(NpcGoal.Grind, result.Goal);
        Assert.Equal(TimeScale.MaxYear, npc.CurrentYear);
    }

    [Fact]
    public void Act_DefaultMonsterRoster_FallsBackToTestMonstersWhenNoneIsGiven()
    {
        var npc = FreshNpc();

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5));

        Assert.NotNull(result.MonsterName);
        Assert.Contains(TestMonsters.All, factory => factory().Name == result.MonsterName);
    }

    [Fact]
    public void Act_ExplicitMonsterRoster_FightsFromThatRosterInstead()
    {
        var npc = FreshNpc();
        var customRoster = TestMonsters.RosterFor(3);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5), monsterRoster: customRoster);

        Assert.NotNull(result.MonsterName);
        Assert.Contains(customRoster, factory => factory().Name == result.MonsterName);
    }

    [Fact]
    public void Act_LocalPoolNotAtAnchor_RollPasses_JumpsStraightToTheAnchorWhenAffordable()
    {
        var npc = ReadyToTravelNpc();
        var world = TestTimeWorld.Build();

        // 0.01 passes the AnchorTravelAttemptChance (0.6) gate.
        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.01), world: world, anchorYear: 2250, pullToAnchor: true);

        Assert.Equal(NpcGoal.Travel, result.Goal);
        Assert.Equal(2250, npc.CurrentYear);
    }

    [Fact]
    public void Act_LocalPoolNotAtAnchor_RollFails_FallsThroughToGrind()
    {
        var npc = ReadyToTravelNpc();
        var world = TestTimeWorld.Build();

        // 0.9 misses the AnchorTravelAttemptChance (0.6) gate.
        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.9), world: world, anchorYear: 2250, pullToAnchor: true);

        Assert.NotEqual(NpcGoal.Travel, result.Goal);
        Assert.Equal(2000, npc.CurrentYear);
    }

    [Fact]
    public void Act_LocalPoolAlreadyAtTheAnchor_DoesNotForceATravelAttempt()
    {
        var npc = ReadyToTravelNpc();
        var world = TestTimeWorld.Build();

        // Already at the anchor, so the anchor-pull branch is skipped entirely;
        // 0.5 also misses the background TravelAttemptChance (0.10) gate.
        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5), world: world, anchorYear: 2000, pullToAnchor: true);

        Assert.NotEqual(NpcGoal.Travel, result.Goal);
        Assert.Equal(2000, npc.CurrentYear);
    }

    [Fact]
    public void Act_LootedWeaponBeatsWhatsEquipped_AutoWieldsItWithoutAStore()
    {
        var npc = FreshNpc();
        var weapon = Item.Create("Rusty Blade", ItemType.Weapon, 1, Rarity.Common);
        npc.AddToInventory(weapon);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5));

        Assert.Equal(NpcGoal.Upgrade, result.Goal);
        Assert.Equal(weapon, npc.EquippedWeapon);
        Assert.Contains(weapon, npc.Inventory);
    }

    [Fact]
    public void Act_LootedWeaponNoBetterThanWhatsEquipped_DoesNotSwap()
    {
        var npc = FreshNpc();
        var goodWeapon = Item.Create("Fine Blade", ItemType.Weapon, 5, Rarity.Rare);
        npc.AddToInventory(goodWeapon);
        npc.Wield(goodWeapon);
        var worseWeapon = Item.Create("Rusty Blade", ItemType.Weapon, 1, Rarity.Common);
        npc.AddToInventory(worseWeapon);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5));

        Assert.NotEqual(NpcGoal.Upgrade, result.Goal);
        Assert.Equal(goodWeapon, npc.EquippedWeapon);
    }

    [Fact]
    public void Act_NoStoreOwnedAndTwoOrMoreVacantSlots_OccasionallyBuysOne()
    {
        var npc = FreshNpc();
        npc.AddCredits(500);
        var vacantA = new StoreSlot("Vacant A", Coordinate.Origin, homeLevel: 1, purchaseCost: 100);
        var vacantB = new StoreSlot("Vacant B", new Coordinate(1, 0), homeLevel: 1, purchaseCost: 100);

        // 0.01 passes StorePurchaseChance (0.05) and, reused as the pick
        // index over 2 vacant slots, selects index 0 (Vacant A).
        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.01), [vacantA, vacantB]);

        Assert.Equal(NpcGoal.OwnStore, result.Goal);
        Assert.False(vacantA.IsAvailableForPurchase);
        Assert.Equal(npc, vacantA.Store!.Owner);
        Assert.True(vacantB.IsAvailableForPurchase);
        Assert.Equal(400, npc.Credits);
    }

    [Fact]
    public void Act_OnlyOneVacantSlotInTheYear_NeverBuysIt_ReservedForThePlayer()
    {
        var npc = FreshNpc();
        npc.AddCredits(500);
        var onlyVacant = new StoreSlot("Last Vacancy", Coordinate.Origin, homeLevel: 1, purchaseCost: 100);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.01), [onlyVacant]);

        Assert.NotEqual(NpcGoal.OwnStore, result.Goal);
        Assert.True(onlyVacant.IsAvailableForPurchase);
        Assert.Equal(500, npc.Credits);
    }

    [Fact]
    public void Act_OwnsAStoreWithALowReserve_PaysMaintenanceBeforeStocking()
    {
        var npc = FreshNpc();
        var junk = Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common);
        npc.AddToInventory(junk);
        var slot = new StoreSlot("Vex's Store", Coordinate.Origin, homeLevel: 1, purchaseCost: 0,
            new Store("Vex's Store", homeLevel: 1, startingCapital: 0, npc, startingTachyonReserve: 0));

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.01), [slot]);

        Assert.Equal(NpcGoal.OwnStore, result.Goal);
        Assert.Equal(30, slot.Store!.TachyonReserve);
        Assert.Contains(junk, npc.Inventory); // untouched - maintenance was paid instead
    }

    [Fact]
    public void Act_OwnsAStoreWithAFullReserve_OnlyJunkSurplus_NeverStocksJunkAtOwnStore()
    {
        // Junk isn't "important to the NPC's class" — TryTendOwnStore only
        // ever lists class-relevant gear at the NPC's own (themed)
        // shopfront (see SelectClassRelevantSurplus); junk is cleared out
        // elsewhere instead (TryTrade), never showcased here. With nothing
        // else to do this tick (one junk item is below TryTrade's
        // excess-junk threshold, and the NPC has no Credits to buy a
        // weapon), the tick falls all the way through to an ordinary
        // grind rather than stocking anything.
        var npc = FreshNpc();
        var junk = Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common);
        npc.AddToInventory(junk);
        var store = new Store("Vex's Store", homeLevel: 1, startingCapital: 0, npc, startingTachyonReserve: 100);
        var slot = new StoreSlot("Vex's Store", Coordinate.Origin, homeLevel: 1, purchaseCost: 0, store);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.01), [slot]);

        Assert.Equal(100, store.TachyonReserve); // unchanged - reserve was already at target
        Assert.Contains(junk, npc.Inventory); // never stocked at the NPC's own store
        Assert.Empty(store.Listings);
    }

    [Fact]
    public void Act_OwnsAStoreWithAFullReserve_StocksClassRelevantGearSurplusInstead()
    {
        var npc = FreshNpc(CharacterClass.Soldier);
        var goodWeapon = Item.Create("Fine Blade", ItemType.Weapon, 5, Rarity.Rare);
        npc.AddToInventory(goodWeapon);
        npc.Wield(goodWeapon); // equipped - FindUpgrade won't touch the weaker surplus below

        // A weaker, unequipped weapon restricted to (and thus fully
        // effective for) this NPC's own class - exactly the "important to
        // its class" surplus TryTendOwnStore is meant to feature.
        var surplusWeapon = Item.Create("Rusty Blade", ItemType.Weapon, 1, Rarity.Common, restrictedClass: CharacterClass.Soldier);
        npc.AddToInventory(surplusWeapon);
        var store = new Store("Vex's Store", homeLevel: 1, startingCapital: 0, npc, startingTachyonReserve: 100);
        var slot = new StoreSlot("Vex's Store", Coordinate.Origin, homeLevel: 1, purchaseCost: 0, store);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.01), [slot]);

        Assert.Equal(NpcGoal.OwnStore, result.Goal);
        Assert.DoesNotContain(surplusWeapon, npc.Inventory);
        Assert.Contains(store.Listings, l => l.Item == surplusWeapon);
    }

    [Fact]
    public void Act_OwnsAStoreWithAFullReserve_OffClassGearSurplus_NeverStockedAtOwnStore()
    {
        // A weapon restricted to a DIFFERENT class (off-class for this
        // NPC, per docs/GDD.md §4.3's wield-at-a-penalty rule) is genuine
        // surplus but never "important to this NPC's class" - it stays out
        // of the NPC's own themed listings even though it's the only
        // wieldable surplus on hand.
        var npc = FreshNpc(CharacterClass.Soldier);
        var offClassWeapon = Item.Create("Arcane Rod", ItemType.Weapon, 1, Rarity.Common, restrictedClass: CharacterClass.Scientist);
        npc.AddToInventory(offClassWeapon);
        var store = new Store("Vex's Store", homeLevel: 1, startingCapital: 0, npc, startingTachyonReserve: 100);
        var slot = new StoreSlot("Vex's Store", Coordinate.Origin, homeLevel: 1, purchaseCost: 0, store);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.01), [slot]);

        Assert.Contains(offClassWeapon, npc.Inventory);
        Assert.Empty(store.Listings);
    }

    [Fact]
    public void Act_OwnsAnOverStockedStore_MarksDownOldestListingInsteadOfStockingMore()
    {
        var npc = FreshNpc(CharacterClass.Soldier);
        var goodWeapon = Item.Create("Fine Blade", ItemType.Weapon, 5, Rarity.Rare);
        npc.AddToInventory(goodWeapon);
        npc.Wield(goodWeapon); // so FindUpgrade won't auto-wield the weaker surplus below
        var store = new Store("Vex's Store", homeLevel: 1, startingCapital: 0, npc, startingTachyonReserve: 100);
        for (var i = 0; i < Store.MaxListings; i++)
        {
            store.Stock(Item.Create($"Filler {i}", ItemType.Junk, 1, Rarity.Common), askingPrice: 1);
        }

        var surplusWeapon = Item.Create("Rusty Blade", ItemType.Weapon, 1, Rarity.Common, restrictedClass: CharacterClass.Soldier);
        npc.AddToInventory(surplusWeapon);
        var slot = new StoreSlot("Vex's Store", Coordinate.Origin, homeLevel: 1, purchaseCost: 0, store);

        // 0.01 enters TryTendOwnStore and clears the StoreClearanceChance gate.
        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.01), [slot]);

        Assert.Equal(NpcGoal.OwnStore, result.Goal);
        Assert.Contains(surplusWeapon, npc.Inventory);                 // shelf over the cap - not deposited
        Assert.Equal(Store.MaxListings - 1, store.Listings.Count);      // oldest unsold listing marked down
        Assert.DoesNotContain(store.Listings, l => l.Item.Name == "Filler 0");
    }

    [Fact]
    public void Act_OwnsAStoreUnderTheSoftCap_StillStocksClassRelevantSurplus()
    {
        var npc = FreshNpc(CharacterClass.Soldier);
        var goodWeapon = Item.Create("Fine Blade", ItemType.Weapon, 5, Rarity.Rare);
        npc.AddToInventory(goodWeapon);
        npc.Wield(goodWeapon); // so FindUpgrade won't auto-wield the weaker surplus below
        var store = new Store("Vex's Store", homeLevel: 1, startingCapital: 0, npc, startingTachyonReserve: 100);
        store.Stock(Item.Create("Filler", ItemType.Junk, 1, Rarity.Common), askingPrice: 1);

        var surplusWeapon = Item.Create("Rusty Blade", ItemType.Weapon, 1, Rarity.Common, restrictedClass: CharacterClass.Soldier);
        npc.AddToInventory(surplusWeapon);
        var slot = new StoreSlot("Vex's Store", Coordinate.Origin, homeLevel: 1, purchaseCost: 0, store);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.01), [slot]);

        Assert.Equal(NpcGoal.OwnStore, result.Goal);
        Assert.DoesNotContain(surplusWeapon, npc.Inventory);
        Assert.Contains(store.Listings, l => l.Item == surplusWeapon);
    }

    [Fact]
    public void Act_SurplusGearAndExcessJunkBothPresent_SellsTheGearFirst()
    {
        var npc = FreshNpc();
        var goodWeapon = Item.Create("Fine Blade", ItemType.Weapon, 5, Rarity.Rare);
        npc.AddToInventory(goodWeapon);
        npc.Wield(goodWeapon);
        var surplusWeapon = Item.Create("Rusty Blade", ItemType.Weapon, 1, Rarity.Common);
        npc.AddToInventory(surplusWeapon);
        for (var tier = 1; tier <= 4; tier++)
        {
            npc.AddToInventory(Item.Create($"Junk Tier {tier}", ItemType.Junk, tier, Rarity.Common));
        }

        var store = Store.CreateGovernmentStore("Test Store", homeLevel: 1);

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5), [OccupiedSlot(store)]);

        Assert.Equal(NpcGoal.Trade, result.Goal);
        Assert.DoesNotContain(surplusWeapon, npc.Inventory);
        Assert.Equal(4, npc.Inventory.Count(i => i.Type == ItemType.Junk));
        Assert.Equal(goodWeapon, npc.EquippedWeapon);
    }

    private static AbilityData HealAbility(string characterClass, int level = 5, double magnitude = 0.35, int tachyonCost = 8) => new()
    {
        Class = characterClass,
        Tier = 1,
        Level = level,
        Name = "Triage",
        Description = "test fixture",
        Effect = "Heal",
        Magnitude = magnitude,
        TachyonCost = tachyonCost,
    };

    [Fact]
    public void Act_Grind_WithUsableAbility_CastsItInsteadOfOnlyAttacking()
    {
        // Level 5 unlocks Triage; damage the NPC down to ~35% HP - above
        // the outer Retreat gate (30%) so Act still reaches the grind
        // branch, but below the in-fight emergency-heal threshold (40%) so
        // the very first round always evaluates casting regardless of the
        // random roll.
        var npc = FreshNpc(CharacterClass.Doctor);
        for (var i = 1; i < 5; i++)
        {
            npc.LevelUp();
        }

        // A deliberately huge HP pool (rather than the class's real, modest
        // curve) so the fight can't be killed off by an unlucky monster
        // roll before the emergency heal ever fires — this test only cares
        // that a usable Heal ability gets cast, not about real balance.
        npc.Health.SetMax(1000);
        npc.Health.Heal(npc.Health.Max);
        npc.Health.Damage((int)(npc.Health.Max * 0.65)); // ~35% - above Act's outer Retreat gate (30%), below the in-fight emergency-heal threshold (40%)
        npc.Tachyons.SetMax(200);
        npc.Tachyons.Add(200);

        var abilities = new List<AbilityData> { HealAbility("Doctor") };

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.01), abilities: abilities);

        Assert.Equal(NpcGoal.Grind, result.Goal);
        Assert.NotNull(result.Fight);
        Assert.Contains(result.Fight!.Log, line => line.Contains("heals for", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Act_Grind_AbilityWrongClass_NeverCastsAndJustAttacks()
    {
        var npc = FreshNpc(CharacterClass.Soldier);
        for (var i = 1; i < 5; i++)
        {
            npc.LevelUp();
        }

        npc.Health.SetMax(1000);
        npc.Health.Heal(npc.Health.Max);
        npc.Health.Damage((int)(npc.Health.Max * 0.65));
        npc.Tachyons.SetMax(200);
        npc.Tachyons.Add(200);

        // A Doctor-only ability on a Soldier NPC is never usable.
        var abilities = new List<AbilityData> { HealAbility("Doctor") };

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.01), abilities: abilities);

        Assert.Equal(NpcGoal.Grind, result.Goal);
        Assert.NotNull(result.Fight);
        Assert.DoesNotContain(result.Fight!.Log, line => line.Contains("heals for", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Fight!.Log, line => line.Contains("hits", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Act_Grind_AbilityBelowLevel_NeverCastsAndJustAttacks()
    {
        var npc = FreshNpc(CharacterClass.Doctor); // level 1 - Triage unlocks at 5
        npc.Health.Damage((int)(npc.Health.Max * 0.65));
        npc.Tachyons.SetMax(200);
        npc.Tachyons.Add(200);

        var abilities = new List<AbilityData> { HealAbility("Doctor") };

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.01), abilities: abilities);

        Assert.Equal(NpcGoal.Grind, result.Goal);
        Assert.NotNull(result.Fight);
        Assert.DoesNotContain(result.Fight!.Log, line => line.Contains("heals for", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Act_Grind_NoAbilitiesGiven_FightsWithTheOriginalAttackOnlyResolver()
    {
        var npc = FreshNpc();

        var result = NpcController.Act(npc, TestLevelMap, StubRandomSource.Fixed(0.5));

        Assert.Equal(NpcGoal.Grind, result.Goal);
        Assert.NotNull(result.Fight);
        Assert.Contains(result.Fight!.Log, line => line.Contains("hits", StringComparison.OrdinalIgnoreCase));
    }
}
