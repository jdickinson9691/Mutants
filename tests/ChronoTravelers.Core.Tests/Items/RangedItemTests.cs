using ChronoTravelers.Core.Tachyons;
using ChronoTravelers.Core.Items;

namespace ChronoTravelers.Core.Tests.Items;

public class RangedItemTests
{
    [Fact]
    public void CreateRanged_ProducesAFullMagazineRangedItemWithAUniqueId()
    {
        var bow = Item.CreateRanged("Longbow", tier: 2, Rarity.Uncommon, RangedKind.Bow, ammoCapacity: 10);

        Assert.Equal(ItemType.Ranged, bow.Type);
        Assert.True(bow.IsRanged);
        Assert.False(bow.IsDepleted);
        Assert.Equal(10, bow.AmmoCapacity);
        Assert.Equal(10, bow.AmmoRemaining);
        Assert.NotEqual(Guid.Empty, bow.InstanceId);
        Assert.True(bow.AttackBonus > 0);
        Assert.True(bow.IsWieldable);
    }

    [Fact]
    public void CreateRanged_EachCallIsADistinctInstance_EvenWithIdenticalStats()
    {
        var a = Item.CreateRanged("Wand", 3, Rarity.Rare, RangedKind.Wand, 5, RangedEffectType.Weaken, 3);
        var b = Item.CreateRanged("Wand", 3, Rarity.Rare, RangedKind.Wand, 5, RangedEffectType.Weaken, 3);

        Assert.NotEqual(a, b);
        Assert.NotEqual(a.InstanceId, b.InstanceId);
    }

    [Fact]
    public void NonRangedItems_KeepPlainValueEquality()
    {
        var a = Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common);
        var b = Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common);

        Assert.Equal(a, b);
        Assert.Equal(Guid.Empty, a.InstanceId);
        Assert.Equal(0, a.AmmoRemaining);
        Assert.False(a.IsRanged);
    }

    [Fact]
    public void IsDepleted_FlipsWhenAmmoRunsOut()
    {
        var gun = Item.CreateRanged("Pistol", 4, Rarity.Rare, RangedKind.Gun, ammoCapacity: 3);

        Assert.False(gun.IsDepleted);
        gun.AmmoRemaining = 0;
        Assert.True(gun.IsDepleted);
    }

    [Fact]
    public void ConvertAndSellValue_ScaleDownAsAmmoIsSpent_ButNeverBelowTheFloor()
    {
        var full = Item.CreateRanged("Rifle", 5, Rarity.Rare, RangedKind.Gun, ammoCapacity: 10);
        var half = Item.CreateRanged("Rifle", 5, Rarity.Rare, RangedKind.Gun, ammoCapacity: 10);
        half.AmmoRemaining = 5;
        var empty = Item.CreateRanged("Rifle", 5, Rarity.Rare, RangedKind.Gun, ammoCapacity: 10);
        empty.AmmoRemaining = 0;

        Assert.Equal(1.0, full.ValueFraction, precision: 6);
        Assert.Equal(0.625, half.ValueFraction, precision: 6); // 0.25 + 0.75*0.5
        Assert.Equal(0.25, empty.ValueFraction, precision: 6);

        Assert.True(full.SellValue() > half.SellValue());
        Assert.True(half.SellValue() > empty.SellValue());
        Assert.True(full.ConvertValue() > empty.ConvertValue());
        Assert.True(empty.ConvertValue() >= 1);
        Assert.True(empty.SellValue() >= 1);

        // A fresh full ranged weapon converts for the same as a plain item of its Value would.
        Assert.Equal(TachyonEconomy.ConvertValue(full.Value), full.ConvertValue());
    }

    [Fact]
    public void NonRangedItem_ValueFractionIsOneAndValuesAreUnscaled()
    {
        var plate = Item.Create("Plating", ItemType.Armor, 3, Rarity.Uncommon);
        Assert.Equal(1.0, plate.ValueFraction, precision: 6);
        Assert.Equal(plate.Value, plate.SellValue());
    }
}
