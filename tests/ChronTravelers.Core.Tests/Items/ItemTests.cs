using ChronTravelers.Core.Classes;
using ChronTravelers.Core.Items;

namespace ChronTravelers.Core.Tests.Items;

public class ItemTests
{
    [Fact]
    public void Create_HigherTierYieldsHigherValueAtSameRarity()
    {
        var lowTier = Item.Create("Rusty Shiv", ItemType.Weapon, tier: 1, Rarity.Common);
        var highTier = Item.Create("Void Shiv", ItemType.Weapon, tier: 5, Rarity.Common);

        Assert.True(highTier.Value > lowTier.Value);
    }

    [Fact]
    public void Create_HigherRarityYieldsHigherValueAtSameTier()
    {
        var common = Item.Create("Plain Blade", ItemType.Weapon, tier: 3, Rarity.Common);
        var legendary = Item.Create("Blade of Ages", ItemType.Weapon, tier: 3, Rarity.Legendary);

        Assert.True(legendary.Value > common.Value);
    }

    [Fact]
    public void ConvertValue_MatchesIonEconomyFormula()
    {
        var item = Item.Create("Junk Scrap", ItemType.Junk, tier: 2, Rarity.Common); // value = 34

        Assert.Equal(13, item.ConvertValue()); // floor(34 * 0.4) = 13
    }

    [Fact]
    public void IsWieldable_TrueOnlyForWeaponsAndArmor()
    {
        var weapon = Item.Create("Sword", ItemType.Weapon, 1, Rarity.Common);
        var armor = Item.Create("Plate", ItemType.Armor, 1, Rarity.Common);
        var potion = Item.Create("Elixir", ItemType.Consumable, 1, Rarity.Common);
        var junk = Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common);

        Assert.True(weapon.IsWieldable);
        Assert.True(armor.IsWieldable);
        Assert.False(potion.IsWieldable);
        Assert.False(junk.IsWieldable);
    }

    [Fact]
    public void IsClassCompatible_TrueWhenUnrestrictedOrMatching()
    {
        var unrestricted = Item.Create("Generic Dagger", ItemType.Weapon, 1, Rarity.Common);
        var warriorOnly = Item.Create("Great Axe", ItemType.Weapon, 1, Rarity.Common, CharacterClass.Soldier);

        Assert.True(unrestricted.IsClassCompatible(CharacterClass.Scientist));
        Assert.True(warriorOnly.IsClassCompatible(CharacterClass.Soldier));
        Assert.False(warriorOnly.IsClassCompatible(CharacterClass.Scientist));
    }

    [Fact]
    public void WieldEffectiveness_FullForCompatible_PenalizedForIncompatible()
    {
        var warriorOnly = Item.Create("Great Axe", ItemType.Weapon, 1, Rarity.Common, CharacterClass.Soldier);

        Assert.Equal(1.0, warriorOnly.WieldEffectiveness(CharacterClass.Soldier));
        Assert.True(warriorOnly.WieldEffectiveness(CharacterClass.Scientist) < 1.0);
    }

    [Fact]
    public void Create_WeaponsGetAttackBonusOnly()
    {
        var weapon = Item.Create("Sword", ItemType.Weapon, 2, Rarity.Common);

        Assert.True(weapon.AttackBonus > 0);
        Assert.Equal(0, weapon.DefenseBonus);
    }

    [Fact]
    public void Create_ArmorGetsDefenseBonusOnly()
    {
        var armor = Item.Create("Plate", ItemType.Armor, 2, Rarity.Common);

        Assert.Equal(0, armor.AttackBonus);
        Assert.True(armor.DefenseBonus > 0);
    }

    [Fact]
    public void Create_NonEquipmentGetsNoCombatBonus()
    {
        var potion = Item.Create("Elixir", ItemType.Consumable, 2, Rarity.Common);
        var junk = Item.Create("Scrap", ItemType.Junk, 2, Rarity.Common);

        Assert.Equal(0, potion.AttackBonus);
        Assert.Equal(0, potion.DefenseBonus);
        Assert.Equal(0, junk.AttackBonus);
        Assert.Equal(0, junk.DefenseBonus);
    }

    [Fact]
    public void SellValue_EqualsItemValue()
    {
        var item = Item.Create("Scrap Metal", ItemType.Junk, tier: 2, Rarity.Common); // value 20
        Assert.Equal(item.Value, item.SellValue());
    }

    [Fact]
    public void IsUsable_TrueOnlyForAConsumableWithARealEffect()
    {
        var foodWithEffect = Item.Create("Ration Pack", ItemType.Consumable, 1, Rarity.Common, consumableEffect: ConsumableEffectType.Heal, effectMagnitude: 10);
        var flavorOnlyConsumable = Item.Create("Trinket", ItemType.Consumable, 1, Rarity.Common); // no effect specified - defaults to None
        var weaponWithNoEffect = Item.Create("Sword", ItemType.Weapon, 1, Rarity.Common);

        Assert.True(foodWithEffect.IsUsable);
        Assert.False(flavorOnlyConsumable.IsUsable);
        Assert.False(weaponWithNoEffect.IsUsable);
    }
}
