using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Economy;
using ChronoTravelers.Core.Items;

namespace ChronoTravelers.Core.Tests.Items;

/// <summary>
/// Unit coverage for docs/GDD.md §6.3's "repair costs" Credit sink — the
/// Weapon/Armor Durability model (ChronoTravelers.Core.Items.Item's
/// MaxDurability/Durability/HasDurability/IsBroken/DurabilityEffectiveness),
/// its pricing (EconomyPricing.RepairCost), and the store-side repair verb
/// (Store.Repair). Combat's actual wear-per-hit is covered by
/// ChronoTravelers.Engine.Tests.Combat.CombatResolverTests instead, since it
/// needs a real fight to exercise.
/// </summary>
public class DurabilityTests
{
    private static Traveler NewTraveler(string name = "Rook") => new(name, CharacterClass.Soldier);

    [Fact]
    public void Create_WeaponAndArmor_StartAtFullDurability()
    {
        var weapon = Item.Create("Scrap Blade", ItemType.Weapon, tier: 3, Rarity.Common);
        var armor = Item.Create("Scrap Vest", ItemType.Armor, tier: 3, Rarity.Common);

        Assert.True(weapon.HasDurability);
        Assert.True(armor.HasDurability);
        Assert.True(weapon.MaxDurability > 0);
        Assert.Equal(weapon.MaxDurability, weapon.Durability);
        Assert.Equal(armor.MaxDurability, armor.Durability);
        Assert.Equal(1.0, weapon.DurabilityEffectiveness);
        Assert.False(weapon.IsBroken);
    }

    [Theory]
    [InlineData(ItemType.Consumable)]
    [InlineData(ItemType.Junk)]
    public void Create_NonEquipmentTypes_NeverTrackDurability(ItemType type)
    {
        var item = Item.Create("Field Ration", type, tier: 2, Rarity.Common);

        Assert.False(item.HasDurability);
        Assert.Equal(0, item.MaxDurability);
        Assert.Equal(1.0, item.DurabilityEffectiveness);
    }

    [Fact]
    public void RangedWeapon_NeverTracksDurability_UsesItsOwnAmmoModelInstead()
    {
        var wand = Item.CreateRanged("Static Wand", tier: 2, Rarity.Common, RangedKind.Wand, ammoCapacity: 5);

        Assert.False(wand.HasDurability);
        Assert.Equal(1.0, wand.DurabilityEffectiveness);
    }

    [Fact]
    public void StarterGear_BuiltWithoutItemCreate_IsExemptFromDurability()
    {
        // Mirrors ChronoTravelers.Game.CharacterFactory.NewTraveler's starter
        // weapon/armor construction — MaxDurability's 0 default means
        // starter gear never wears out or needs repair.
        var starterWeapon = new Item("Standard-Issue Baton", ItemType.Weapon, 1, Rarity.Common, Value: 5, AttackBonus: 10);

        Assert.False(starterWeapon.HasDurability);
        Assert.Equal(0, EconomyPricing.RepairCost(starterWeapon));
    }

    [Fact]
    public void DurabilityEffectiveness_ScalesLinearlyWithNoFloor()
    {
        var weapon = Item.Create("Scrap Blade", ItemType.Weapon, tier: 3, Rarity.Common);
        var half = weapon.MaxDurability / 2;
        weapon.Durability = half;

        Assert.Equal(half / (double)weapon.MaxDurability, weapon.DurabilityEffectiveness, precision: 6);

        weapon.Durability = 0;

        Assert.True(weapon.IsBroken);
        Assert.Equal(0.0, weapon.DurabilityEffectiveness);
    }

    [Fact]
    public void ValueFraction_FloorsAt25PercentEvenWhenBroken()
    {
        var weapon = Item.Create("Scrap Blade", ItemType.Weapon, tier: 3, Rarity.Common);
        weapon.Durability = 0;

        // Unlike DurabilityEffectiveness (combat usefulness, no floor),
        // ValueFraction (scrap value) never drops below 25% — mirrors the
        // ranged-weapon ammo-depletion curve.
        Assert.Equal(0.25, weapon.ValueFraction, precision: 6);
        Assert.True(weapon.SellValue() > 0);
    }

    [Fact]
    public void EquippedBrokenWeapon_ContributesNoAttackBonus()
    {
        var traveler = NewTraveler();
        var weapon = Item.Create("Scrap Blade", ItemType.Weapon, tier: 3, Rarity.Common);
        traveler.AddToInventory(weapon);
        traveler.Wield(weapon);

        var attackWithFullDurability = traveler.EffectiveAttackPower;

        weapon.Durability = 0;

        Assert.True(traveler.EffectiveAttackPower < attackWithFullDurability);
    }

    [Fact]
    public void EquippedBrokenArmor_ContributesNoDefenseBonus()
    {
        var traveler = NewTraveler();
        var armor = Item.Create("Scrap Vest", ItemType.Armor, tier: 3, Rarity.Common);
        traveler.AddToInventory(armor);
        traveler.Wield(armor);

        var defenseWithFullDurability = traveler.EffectiveDefense;

        armor.Durability = 0;

        Assert.True(traveler.EffectiveDefense < defenseWithFullDurability);
    }

    [Fact]
    public void RepairCost_IsZero_ForAnItemAtFullDurability()
    {
        var weapon = Item.Create("Scrap Blade", ItemType.Weapon, tier: 3, Rarity.Common);

        Assert.Equal(0, EconomyPricing.RepairCost(weapon));
    }

    [Fact]
    public void RepairCost_IsProportionalToWear()
    {
        var weapon = Item.Create("Scrap Blade", ItemType.Weapon, tier: 5, Rarity.Rare);
        weapon.Durability = weapon.MaxDurability / 2; // half-worn
        var halfWornCost = EconomyPricing.RepairCost(weapon);

        weapon.Durability = 0; // fully broken
        var fullyBrokenCost = EconomyPricing.RepairCost(weapon);

        Assert.True(halfWornCost > 0);
        Assert.True(fullyBrokenCost > halfWornCost);
        // A full repair is priced as a fraction of Value (see
        // EconomyPricing's FullRepairCostFraction) — cheaper than the item
        // itself, so repairing is always worth it over replacing.
        Assert.True(fullyBrokenCost < weapon.Value);
    }

    [Fact]
    public void StoreRepair_RestoresDurabilityAndChargesCredits()
    {
        var store = Store.CreateGovernmentStore("Fix-It Depot", homeLevel: 1);
        var traveler = NewTraveler();
        traveler.AddCredits(10_000);
        var weapon = Item.Create("Scrap Blade", ItemType.Weapon, tier: 3, Rarity.Common);
        weapon.Durability = 0;
        var creditsBefore = traveler.Credits;

        var paid = store.Repair(traveler, weapon);

        Assert.NotNull(paid);
        Assert.True(paid > 0);
        Assert.Equal(weapon.MaxDurability, weapon.Durability);
        Assert.Equal(creditsBefore - paid, traveler.Credits);
    }

    [Fact]
    public void StoreRepair_DoesNotMoveCreditsIntoStoreCapital()
    {
        // docs/GDD.md §6.3: repair is a Credit sink, not a trade — unlike
        // BuyFromTraveler/SellToTraveler, the Credits paid just leave the
        // economy rather than landing in the store's Capital.
        var store = Store.CreateGovernmentStore("Fix-It Depot", homeLevel: 1);
        var traveler = NewTraveler();
        traveler.AddCredits(10_000);
        var weapon = Item.Create("Scrap Blade", ItemType.Weapon, tier: 3, Rarity.Common);
        weapon.Durability = 0;
        var capitalBefore = store.Capital;

        store.Repair(traveler, weapon);

        Assert.Equal(capitalBefore, store.Capital);
    }

    [Fact]
    public void StoreRepair_RefusesWhenTravelerCantAfford()
    {
        var store = Store.CreateGovernmentStore("Fix-It Depot", homeLevel: 1);
        var traveler = NewTraveler(); // starts with 0 Credits
        var weapon = Item.Create("Scrap Blade", ItemType.Weapon, tier: 5, Rarity.Legendary);
        weapon.Durability = 0;

        var paid = store.Repair(traveler, weapon);

        Assert.Null(paid);
        Assert.Equal(0, weapon.Durability); // untouched
    }

    [Fact]
    public void StoreRepair_RefusesAnItemThatDoesntTrackDurability()
    {
        var store = Store.CreateGovernmentStore("Fix-It Depot", homeLevel: 1);
        var traveler = NewTraveler();
        traveler.AddCredits(10_000);
        var potion = Item.Create("Field Ration", ItemType.Consumable, 1, Rarity.Common);

        var paid = store.Repair(traveler, potion);

        Assert.Null(paid);
    }

    [Fact]
    public void StoreRepair_RefusesAnItemAlreadyAtFullDurability()
    {
        var store = Store.CreateGovernmentStore("Fix-It Depot", homeLevel: 1);
        var traveler = NewTraveler();
        traveler.AddCredits(10_000);
        var weapon = Item.Create("Scrap Blade", ItemType.Weapon, tier: 3, Rarity.Common);

        var paid = store.Repair(traveler, weapon);

        Assert.Null(paid);
    }
}
