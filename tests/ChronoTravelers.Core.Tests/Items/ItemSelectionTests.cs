using ChronoTravelers.Core.Items;

namespace ChronoTravelers.Core.Tests.Items;

/// <summary>
/// Coverage for <see cref="ItemSelection.Weakest"/> — the ordering that lets
/// a destructive command like `convert` resolve a name match to the worst
/// same-named copy rather than an arbitrary one. See ItemSelection.cs's own
/// doc comment for the motivating scenario (multiple Time Shards, one per
/// visited year).
/// </summary>
public class ItemSelectionTests
{
    [Fact]
    public void Weakest_PicksTheLowerTierItem_WhenTiersDiffer()
    {
        var lowTier = Item.Create("Time Shard", ItemType.Weapon, tier: 1, Rarity.Common);
        var highTier = Item.Create("Time Shard", ItemType.Weapon, tier: 5, Rarity.Common);

        var weakest = ItemSelection.Weakest([highTier, lowTier]);

        Assert.Same(lowTier, weakest);
    }

    [Fact]
    public void Weakest_FallsBackToDamage_WhenTiersAreEqual()
    {
        var strong = new Item("Rusty Blade", ItemType.Weapon, Tier: 2, Rarity.Common, Value: 10, AttackBonus: 8);
        var weak = new Item("Rusty Blade", ItemType.Weapon, Tier: 2, Rarity.Common, Value: 10, AttackBonus: 2);

        var weakest = ItemSelection.Weakest([strong, weak]);

        Assert.Same(weak, weakest);
    }

    [Fact]
    public void Weakest_FallsBackToDefense_WhenTierAndAttackAreEqual()
    {
        var strong = new Item("Old Plate", ItemType.Armor, Tier: 2, Rarity.Common, Value: 10, DefenseBonus: 8);
        var weak = new Item("Old Plate", ItemType.Armor, Tier: 2, Rarity.Common, Value: 10, DefenseBonus: 2);

        var weakest = ItemSelection.Weakest([strong, weak]);

        Assert.Same(weak, weakest);
    }

    [Fact]
    public void Weakest_PrefersTheMoreDepletedRangedWeapon_WhenOtherwiseTied()
    {
        var fullAmmo = new Item("Marshal's Repeater", ItemType.Ranged, Tier: 2, Rarity.Common, Value: 10) { AmmoRemaining = 6 };
        var lowAmmo = new Item("Marshal's Repeater", ItemType.Ranged, Tier: 2, Rarity.Common, Value: 10) { AmmoRemaining = 1 };

        var weakest = ItemSelection.Weakest([fullAmmo, lowAmmo]);

        Assert.Same(lowAmmo, weakest);
    }

    [Fact]
    public void Weakest_IsANoOp_ForIdenticalDuplicates()
    {
        var a = Item.Create("Salvage Shard", ItemType.Junk, tier: 1, Rarity.Common);
        var b = Item.Create("Salvage Shard", ItemType.Junk, tier: 1, Rarity.Common);

        var weakest = ItemSelection.Weakest([a, b]);

        // Neither is objectively weaker — either is an acceptable pick, but
        // the call must not throw and must return one of the candidates.
        Assert.True(ReferenceEquals(weakest, a) || ReferenceEquals(weakest, b));
    }

    [Fact]
    public void Weakest_WithASingleItem_ReturnsIt()
    {
        var only = Item.Create("Lone Shard", ItemType.Weapon, tier: 1, Rarity.Common);

        Assert.Same(only, ItemSelection.Weakest([only]));
    }

    [Fact]
    public void Weakest_WithNoItems_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ItemSelection.Weakest([]));
    }
}
