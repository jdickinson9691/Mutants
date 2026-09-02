using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Stats;
using ChronoTravelers.Core.World;

namespace ChronoTravelers.Core.Tests.Characters;

public class TravelerTests
{
    [Fact]
    public void Constructor_StartsAtLevelOneWithClassBaseStatsAndFullPools()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);

        Assert.Equal(1, traveler.Level);
        Assert.Equal(0, traveler.Xp);
        Assert.Equal(ClassDefinition.For(CharacterClass.Soldier).BaseStats, traveler.Stats);
        Assert.Equal(traveler.Health.Max, traveler.Health.Current);
        Assert.Equal(traveler.Tachyons.Max, traveler.Tachyons.Current);
    }

    [Fact]
    public void Constructor_RejectsEmptyName()
    {
        Assert.Throws<ArgumentException>(() => new Traveler("", CharacterClass.Soldier));
    }

    [Fact]
    public void LevelUp_GrowsEveryStat_PrimaryFaster_AndMaxPools()
    {
        var traveler = new Traveler("Rook", CharacterClass.Scientist); // primary: Intellect
        var before = traveler.Stats;
        var startingMaxHp = traveler.Health.Max;
        var startingMaxTachyons = traveler.Tachyons.Max;

        traveler.LevelUp();

        Assert.Equal(2, traveler.Level);
        Assert.Equal(before.Intellect + Leveling.PrimaryStatGainPerLevel, traveler.Stats.Intellect);
        Assert.Equal(before.Strength + Leveling.SecondaryStatGainPerLevel, traveler.Stats.Strength);
        Assert.Equal(before.Agility + Leveling.SecondaryStatGainPerLevel, traveler.Stats.Agility);
        Assert.Equal(before.Resolve + Leveling.SecondaryStatGainPerLevel, traveler.Stats.Resolve);
        Assert.True(traveler.Health.Max > startingMaxHp);
        Assert.True(traveler.Tachyons.Max > startingMaxTachyons);
    }

    [Fact]
    public void GainXp_AppliesMultipleLevelUpsWhenEnoughXpIsAwardedAtOnce()
    {
        var traveler = new Traveler("Rook", CharacterClass.Spy);
        var xpForLevel3 = Leveling.CumulativeXpForLevel(3);

        var levelsGained = traveler.GainXp(xpForLevel3);

        Assert.Equal(2, levelsGained);
        Assert.Equal(3, traveler.Level);
    }

    [Fact]
    public void GainXp_StopsAtSoftLevelCapForFurthestYearReached()
    {
        // Year 2000 -> soft cap of character level 10.
        var traveler = new Traveler("Rook", CharacterClass.Doctor, startingYear: 2000);

        traveler.GainXp(Leveling.CumulativeXpForLevel(Leveling.MaxCharacterLevel));

        Assert.Equal(10, traveler.Level);
    }

    [Fact]
    public void SetCurrentYear_RaisesFurthestYearReachedButNeverRegressesIt()
    {
        var traveler = new Traveler("Rook", CharacterClass.Doctor, startingYear: 2000);

        traveler.SetCurrentYear(3125);
        Assert.Equal(3125, traveler.CurrentYear);
        Assert.Equal(3125, traveler.FurthestYearReached);

        traveler.SetCurrentYear(2100); // retreat: current moves, furthest doesn't
        Assert.Equal(2100, traveler.CurrentYear);
        Assert.Equal(3125, traveler.FurthestYearReached);
    }

    [Fact]
    public void Convert_RemovesItemFromInventoryAndAddsTachyons()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        traveler.Tachyons.Spend(traveler.Tachyons.Current); // drain to 0 so Add() has headroom to observe
        var item = Item.Create("Scrap Metal", ItemType.Junk, tier: 1, Rarity.Common); // value 22
        traveler.AddToInventory(item);

        var gained = traveler.Convert(item);

        Assert.Equal(52, gained); // junk -> trash rate: floor(22 * 2.4) = 52
        Assert.DoesNotContain(item, traveler.Inventory);
        Assert.Equal(52, traveler.Tachyons.Current);
    }

    [Fact]
    public void PlayerTachyonPool_IsUncapped_SoConvertingStockpilesPastTheNominalMax()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        Assert.True(traveler.Tachyons.Uncapped);
        var nominalMax = traveler.Tachyons.Max; // starts full at the nominal max

        for (var i = 0; i < 20; i++)
        {
            var item = Item.Create($"Scrap {i}", ItemType.Junk, tier: 3, Rarity.Common);
            traveler.AddToInventory(item);
            traveler.Convert(item);
        }

        Assert.True(traveler.Tachyons.Current > nominalMax * 2,
            $"converting a pile of loot should stockpile Tachyons well past the nominal {nominalMax}");
    }

    [Fact]
    public void Convert_ThrowsIfItemNotInInventory()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var item = Item.Create("Ghost Item", ItemType.Junk, 1, Rarity.Common);

        Assert.Throws<InvalidOperationException>(() => traveler.Convert(item));
    }

    [Fact]
    public void Consume_HealEffect_RemovesItemAndRestoresFlatHp()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        traveler.Health.Damage(15);
        var ration = Item.Create("Ration Pack", ItemType.Consumable, 1, Rarity.Common, consumableEffect: ConsumableEffectType.Heal, effectMagnitude: 10);
        traveler.AddToInventory(ration);

        var healed = traveler.Consume(ration);

        Assert.Equal(10, healed);
        Assert.DoesNotContain(ration, traveler.Inventory);
        Assert.Equal(traveler.Health.Max - 5, traveler.Health.Current);
    }

    [Fact]
    public void Consume_HealEffect_NeverOverheals()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        traveler.Health.Damage(2);
        var ration = Item.Create("Ration Pack", ItemType.Consumable, 1, Rarity.Common, consumableEffect: ConsumableEffectType.Heal, effectMagnitude: 50);
        traveler.AddToInventory(ration);

        var healed = traveler.Consume(ration);

        Assert.Equal(2, healed); // only the 2 missing HP, not the full 50
        Assert.Equal(traveler.Health.Max, traveler.Health.Current);
    }

    [Fact]
    public void Consume_BuffAttackEffect_AddsATimedActiveEffectAndBoostsEffectiveAttack()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var attackBefore = traveler.EffectiveAttackPower;
        var potion = Item.Create("Adrenal Stim", ItemType.Consumable, 2, Rarity.Uncommon, consumableEffect: ConsumableEffectType.BuffAttack, effectMagnitude: 4, effectDurationTicks: 15);
        traveler.AddToInventory(potion);

        traveler.Consume(potion);

        Assert.Equal(attackBefore + 4, traveler.EffectiveAttackPower);
        Assert.Single(traveler.ActiveEffects);
        Assert.Equal(15, traveler.ActiveEffects[0].TicksRemaining);
    }

    [Fact]
    public void Consume_BuffDefenseEffect_BoostsEffectiveDefense()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var defenseBefore = traveler.EffectiveDefense;
        var potion = Item.Create("Patch Kit", ItemType.Consumable, 1, Rarity.Common, consumableEffect: ConsumableEffectType.BuffDefense, effectMagnitude: 3, effectDurationTicks: 15);
        traveler.AddToInventory(potion);

        traveler.Consume(potion);

        Assert.Equal(defenseBefore + 3, traveler.EffectiveDefense);
    }

    [Fact]
    public void Consume_StatElixir_PermanentlyRaisesTheStat_NoTimedEffect()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier); // primary = Strength
        var strBefore = traveler.Stats.Strength;
        var attackBefore = traveler.EffectiveAttackPower;
        var elixir = Item.Create("Meridian Serum: Strength", ItemType.Consumable, 3, Rarity.Epic,
            consumableEffect: ConsumableEffectType.BoostStrength, effectMagnitude: 5);
        traveler.AddToInventory(elixir);

        traveler.Consume(elixir);

        Assert.Equal(strBefore + 5, traveler.Stats.Strength);
        Assert.Equal(attackBefore + 5, traveler.EffectiveAttackPower); // Strength is the Soldier's attack stat
        Assert.Empty(traveler.ActiveEffects);                          // permanent, not a timed buff
        Assert.DoesNotContain(elixir, traveler.Inventory);
    }

    [Fact]
    public void Consume_AgilityElixir_RaisesAgilityDerivedDefenseAndSpeed()
    {
        var traveler = new Traveler("Nyx", CharacterClass.Spy);
        var agiBefore = traveler.Stats.Agility;
        var speedBefore = traveler.Speed;
        var defBefore = traveler.EffectiveDefense;
        var elixir = Item.Create("Meridian Serum: Agility", ItemType.Consumable, 3, Rarity.Epic,
            consumableEffect: ConsumableEffectType.BoostAgility, effectMagnitude: 5);
        traveler.AddToInventory(elixir);

        traveler.Consume(elixir);

        Assert.Equal(agiBefore + 5, traveler.Stats.Agility);
        Assert.Equal(speedBefore + 5, traveler.Speed);                       // speed = Agility
        Assert.Equal(defBefore + ((agiBefore + 5) / 2 - agiBefore / 2), traveler.EffectiveDefense); // defense = Agility / 2
        Assert.True(traveler.EffectiveDefense > defBefore);
    }

    [Fact]
    public void Consume_ThrowsForAnItemThatIsNotUsable()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var junk = Item.Create("Scrap Metal", ItemType.Junk, 1, Rarity.Common);
        traveler.AddToInventory(junk);

        Assert.Throws<InvalidOperationException>(() => traveler.Consume(junk));
        Assert.Contains(junk, traveler.Inventory); // untouched - nothing consumed
    }

    [Fact]
    public void AdvanceEffectTicks_CountsDownAndRemovesExpiredEffects()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var potion = Item.Create("Adrenal Stim", ItemType.Consumable, 2, Rarity.Uncommon, consumableEffect: ConsumableEffectType.BuffAttack, effectMagnitude: 4, effectDurationTicks: 2);
        traveler.AddToInventory(potion);
        traveler.Consume(potion);
        var attackWhileActive = traveler.EffectiveAttackPower;

        traveler.AdvanceEffectTicks();
        Assert.Single(traveler.ActiveEffects);
        Assert.Equal(1, traveler.ActiveEffects[0].TicksRemaining);
        Assert.Equal(attackWhileActive, traveler.EffectiveAttackPower); // still active

        traveler.AdvanceEffectTicks();
        Assert.Empty(traveler.ActiveEffects);
        Assert.True(traveler.EffectiveAttackPower < attackWhileActive); // expired
    }

    [Fact]
    public void Heal_AlreadyAtFullHealth_DoesNothingAndSpendsNoTachyons()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);

        var healed = traveler.Heal();

        Assert.Equal(0, healed);
        Assert.Equal(traveler.Tachyons.Max, traveler.Tachyons.Current);
    }

    [Fact]
    public void Heal_SpendsTachyonsAtTheHealRatioAndFullyRecoversWhenAffordable()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        traveler.Health.Damage(9); // exactly 3 Tachyons' worth at 3:1
        var ionsBefore = traveler.Tachyons.Current;

        var healed = traveler.Heal();

        Assert.Equal(9, healed);
        Assert.Equal(traveler.Health.Max, traveler.Health.Current);
        Assert.Equal(ionsBefore - 3, traveler.Tachyons.Current);
    }

    [Fact]
    public void Heal_NotEnoughTachyonsToFullyHeal_HealsOnlyAsMuchAsAffordable()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        traveler.Health.Damage(25);
        traveler.Tachyons.Spend(traveler.Tachyons.Current - 2); // leave exactly 2 Tachyons -> 6 HP at 3:1

        var healed = traveler.Heal();

        Assert.Equal(6, healed);
        Assert.Equal(traveler.Health.Max - 19, traveler.Health.Current); // healed 6 of the 25 missing
        Assert.Equal(0, traveler.Tachyons.Current);
    }

    [Fact]
    public void Heal_NoTachyonsAvailable_DoesNothing()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        traveler.Health.Damage(10);
        traveler.Tachyons.Spend(traveler.Tachyons.Current);

        var healed = traveler.Heal();

        Assert.Equal(0, healed);
        Assert.Equal(traveler.Health.Max - 10, traveler.Health.Current);
    }

    [Fact]
    public void Wield_EquipsWeaponAndArmorIntoSeparateSlots()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var weapon = Item.Create("Axe", ItemType.Weapon, 1, Rarity.Common);
        var armor = Item.Create("Plate", ItemType.Armor, 1, Rarity.Common);
        traveler.AddToInventory(weapon);
        traveler.AddToInventory(armor);

        traveler.Wield(weapon);
        traveler.Wield(armor);

        Assert.Equal(weapon, traveler.EquippedWeapon);
        Assert.Equal(armor, traveler.EquippedArmor);
    }

    [Fact]
    public void Wield_ThrowsForNonWieldableItem()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var potion = Item.Create("Elixir", ItemType.Consumable, 1, Rarity.Common);
        traveler.AddToInventory(potion);

        Assert.Throws<InvalidOperationException>(() => traveler.Wield(potion));
    }

    [Fact]
    public void Wield_RoutesARangedWeaponToItsOwnSlot_AndUnequipsOnRemove()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var melee = Item.Create("Axe", ItemType.Weapon, 1, Rarity.Common);
        var bow = Item.CreateRanged("Longbow", 1, Rarity.Uncommon, RangedKind.Bow, ammoCapacity: 10);
        traveler.AddToInventory(melee);
        traveler.AddToInventory(bow);

        traveler.Wield(melee);
        traveler.Wield(bow);

        Assert.Equal(melee, traveler.EquippedWeapon);
        Assert.Equal(bow, traveler.EquippedRanged);
        Assert.Null(traveler.EquippedArmor);

        traveler.RemoveFromInventory(bow);
        Assert.Null(traveler.EquippedRanged);
        Assert.Equal(melee, traveler.EquippedWeapon); // untouched
    }

    [Fact]
    public void Wield_AllowsOffClassGearAtAPenalty_RatherThanBlockingIt()
    {
        var mage = new Traveler("Zeta", CharacterClass.Scientist);
        var warriorAxe = Item.Create("Great Axe", ItemType.Weapon, 1, Rarity.Common, CharacterClass.Soldier);
        mage.AddToInventory(warriorAxe);

        mage.Wield(warriorAxe); // must not throw — GDD §4.3 penalty, not hard block

        Assert.Equal(warriorAxe, mage.EquippedWeapon);
        Assert.True(warriorAxe.WieldEffectiveness(CharacterClass.Scientist) < 1.0);
    }

    [Fact]
    public void Wield_ThrowsIfItemNotInInventory()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var weapon = Item.Create("Axe", ItemType.Weapon, 1, Rarity.Common);

        Assert.Throws<InvalidOperationException>(() => traveler.Wield(weapon));
    }

    [Fact]
    public void Position_DefaultsToOrigin()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        Assert.Equal(Coordinate.Origin, traveler.Position);
    }

    [Fact]
    public void PlaceAt_AndMoveTo_UpdatePosition()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var start = new Coordinate(3, 1);
        traveler.PlaceAt(start);
        Assert.Equal(start, traveler.Position);

        var next = start.Move(Direction.North);
        traveler.MoveTo(next);
        Assert.Equal(next, traveler.Position);
    }

    [Fact]
    public void Credits_AddAndSpendTrackBalance()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        Assert.Equal(0, traveler.Credits);

        traveler.AddCredits(50);
        Assert.Equal(50, traveler.Credits);

        traveler.SpendCredits(20);
        Assert.Equal(30, traveler.Credits);
    }

    [Fact]
    public void SpendCredits_ThrowsWhenUnaffordable()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        traveler.AddCredits(10);

        Assert.Throws<InvalidOperationException>(() => traveler.SpendCredits(11));
    }

    [Fact]
    public void Sell_RemovesItemAndAddsCredits()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var item = Item.Create("Scrap Metal", ItemType.Junk, tier: 2, Rarity.Common); // value 34
        traveler.AddToInventory(item);

        var gained = traveler.Sell(item);

        Assert.Equal(34, gained);
        Assert.DoesNotContain(item, traveler.Inventory);
        Assert.Equal(34, traveler.Credits);
    }

    [Fact]
    public void Sell_WithExplicitPrice_OverridesTheFlatRate()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var item = Item.Create("Scrap Metal", ItemType.Junk, tier: 2, Rarity.Common); // flat value 20
        traveler.AddToInventory(item);

        var gained = traveler.Sell(item, credits: 7);

        Assert.Equal(7, gained);
        Assert.Equal(7, traveler.Credits);
    }

    [Fact]
    public void RemoveFromInventory_RemovesWithNoPayout()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var startingTachyons = traveler.Tachyons.Current;
        var item = Item.Create("Scrap Metal", ItemType.Junk, tier: 2, Rarity.Common);
        traveler.AddToInventory(item);

        traveler.RemoveFromInventory(item);

        Assert.DoesNotContain(item, traveler.Inventory);
        Assert.Equal(0, traveler.Credits);
        Assert.Equal(startingTachyons, traveler.Tachyons.Current); // no side effect on Tachyons either
    }

    [Fact]
    public void RemoveFromInventory_UnequipsIfWielded()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var weapon = Item.Create("Axe", ItemType.Weapon, 1, Rarity.Common);
        traveler.AddToInventory(weapon);
        traveler.Wield(weapon);

        traveler.RemoveFromInventory(weapon);

        Assert.Null(traveler.EquippedWeapon);
    }

    [Fact]
    public void Sell_ThrowsIfItemNotInInventory()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var item = Item.Create("Ghost Item", ItemType.Junk, 1, Rarity.Common);

        Assert.Throws<InvalidOperationException>(() => traveler.Sell(item));
    }

    [Fact]
    public void Convert_UnequipsTheItemIfItWasWielded()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var weapon = Item.Create("Axe", ItemType.Weapon, 1, Rarity.Common);
        traveler.AddToInventory(weapon);
        traveler.Wield(weapon);

        traveler.Convert(weapon);

        Assert.Null(traveler.EquippedWeapon);
    }

    [Fact]
    public void Sell_UnequipsTheItemIfItWasWielded()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var armor = Item.Create("Plate", ItemType.Armor, 1, Rarity.Common);
        traveler.AddToInventory(armor);
        traveler.Wield(armor);

        traveler.Sell(armor);

        Assert.Null(traveler.EquippedArmor);
    }

    [Fact]
    public void EffectiveAttackPower_IsPrimaryStatWhenUnarmed()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        Assert.Equal(traveler.Stats.Strength, traveler.EffectiveAttackPower);
    }

    [Fact]
    public void EffectiveAttackPower_AddsFullWeaponBonusForClassCompatibleGear()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var weapon = Item.Create("Axe", ItemType.Weapon, 3, Rarity.Common, CharacterClass.Soldier);
        traveler.AddToInventory(weapon);
        traveler.Wield(weapon);

        Assert.Equal(traveler.Stats.Strength + weapon.AttackBonus, traveler.EffectiveAttackPower);
    }

    [Fact]
    public void EffectiveAttackPower_PenalizesOffClassWeapon()
    {
        var mage = new Traveler("Zeta", CharacterClass.Scientist);
        var warriorAxe = Item.Create("Great Axe", ItemType.Weapon, 3, Rarity.Common, CharacterClass.Soldier);
        mage.AddToInventory(warriorAxe);
        mage.Wield(warriorAxe);

        var fullBonusAttack = mage.Stats.Get(mage.ClassDefinition.PrimaryStat) + warriorAxe.AttackBonus;
        Assert.True(mage.EffectiveAttackPower < fullBonusAttack);
    }

    [Fact]
    public void EffectiveDefense_AddsArmorBonus()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var unarmoredDefense = traveler.EffectiveDefense;

        var armor = Item.Create("Plate", ItemType.Armor, 3, Rarity.Common, CharacterClass.Soldier);
        traveler.AddToInventory(armor);
        traveler.Wield(armor);

        Assert.Equal(unarmoredDefense + armor.DefenseBonus, traveler.EffectiveDefense);
    }

    [Fact]
    public void AdvanceTachyonDrainTick_DoesNothingBeforeTheInterval()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var startingTachyons = traveler.Tachyons.Current;

        var hpLost = traveler.AdvanceTachyonDrainTick(ticksPerDrain: 5);

        Assert.False(hpLost);
        Assert.Equal(startingTachyons, traveler.Tachyons.Current);
    }

    [Fact]
    public void AdvanceTachyonDrainTick_SpendsOneTachyonOnceIntervalElapses()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var startingTachyons = traveler.Tachyons.Current;

        traveler.AdvanceTachyonDrainTick(ticksPerDrain: 3);
        traveler.AdvanceTachyonDrainTick(ticksPerDrain: 3);
        var hpLost = traveler.AdvanceTachyonDrainTick(ticksPerDrain: 3);

        Assert.False(hpLost);
        Assert.Equal(startingTachyons - 1, traveler.Tachyons.Current);
    }

    [Fact]
    public void AdvanceTachyonDrainTick_DamagesHealthWhenOutOfTachyons()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        traveler.Tachyons.Spend(traveler.Tachyons.Current); // drain to 0
        var startingHp = traveler.Health.Current;

        var hpLost = traveler.AdvanceTachyonDrainTick(ticksPerDrain: 1);

        Assert.True(hpLost);
        Assert.Equal(startingHp - 1, traveler.Health.Current);
    }

    [Fact]
    public void AdvanceTachyonDrainTick_RejectsNonPositiveInterval()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        Assert.Throws<ArgumentOutOfRangeException>(() => traveler.AdvanceTachyonDrainTick(0));
    }

    [Fact]
    public void AdvanceTachyonRegenTick_AddsOneTachyonOnceIntervalElapses()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        traveler.Tachyons.Spend(traveler.Tachyons.Current); // drain to 0

        Assert.False(traveler.AdvanceTachyonRegenTick(ticksPerRegen: 3));
        Assert.False(traveler.AdvanceTachyonRegenTick(ticksPerRegen: 3));
        var added = traveler.AdvanceTachyonRegenTick(ticksPerRegen: 3);

        Assert.True(added);
        Assert.Equal(1, traveler.Tachyons.Current);
    }

    [Fact]
    public void AdvanceTachyonRegenTick_ReportsNothingAddedWhenAlreadyAtMax()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier); // starts at full Tachyons

        var added = traveler.AdvanceTachyonRegenTick(ticksPerRegen: 1);

        Assert.False(added);
        Assert.Equal(traveler.Tachyons.Max, traveler.Tachyons.Current);
    }

    [Fact]
    public void AdvanceTachyonRegenTick_RejectsNonPositiveInterval()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        Assert.Throws<ArgumentOutOfRangeException>(() => traveler.AdvanceTachyonRegenTick(0));
    }

    [Fact]
    public void CurrentYear_DefaultsToTheStartOfTheTimeline()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        Assert.Equal(2000, traveler.CurrentYear);
        Assert.Equal(2000, traveler.FurthestYearReached);
    }

    [Fact]
    public void SetCurrentYear_ClampsOutOfRangeYearsToTheTimeline()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        traveler.SetCurrentYear(9999);
        Assert.Equal(5000, traveler.CurrentYear);

        traveler.SetCurrentYear(1000);
        Assert.Equal(2000, traveler.CurrentYear);
    }

    [Fact]
    public void Constructor_RejectsAStartingYearOffTheTimeline()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Traveler("Rook", CharacterClass.Soldier, startingYear: 1900));
    }

    [Fact]
    public void Restore_ReconstructsFullStateExactly()
    {
        var stats = new StatBlock(20, 15, 10, 12);
        var traveler = Traveler.Restore(
            "Rook", CharacterClass.Soldier, level: 7, xp: 555, stats,
            currentHp: 40, maxHp: 60, currentTachyons: 5, maxTachyons: 30, credits: 250,
            currentYear: 2900, furthestYearReached: 3200, position: new Coordinate(2, -1),
            defeatedWardenYears: [2412, 3187]);

        Assert.Equal("Rook", traveler.Name);
        Assert.Equal(CharacterClass.Soldier, traveler.Class);
        Assert.Equal(7, traveler.Level);
        Assert.Equal(555, traveler.Xp);
        Assert.Equal(stats, traveler.Stats);
        Assert.Equal(40, traveler.Health.Current);
        Assert.Equal(60, traveler.Health.Max);
        Assert.Equal(5, traveler.Tachyons.Current);
        Assert.Equal(30, traveler.Tachyons.Max);
        Assert.Equal(250, traveler.Credits);
        Assert.Equal(3200, traveler.FurthestYearReached);
        Assert.Equal(2900, traveler.CurrentYear);
        Assert.Equal(new Coordinate(2, -1), traveler.Position);
        Assert.True(traveler.HasDefeatedWarden(2412));
        Assert.True(traveler.HasDefeatedWarden(3187));
        Assert.False(traveler.HasDefeatedWarden(4000));
        Assert.Empty(traveler.Inventory);
        Assert.Null(traveler.EquippedWeapon);
    }

    [Fact]
    public void Restore_RejectsEmptyName()
    {
        Assert.Throws<ArgumentException>(() => Traveler.Restore(
            "", CharacterClass.Soldier, 1, 0, new StatBlock(10, 10, 10, 10),
            30, 30, 20, 20, 0, 1, 1, Coordinate.Origin, []));
    }

    [Fact]
    public void Restore_ThenAddToInventoryAndWield_WorksNormally()
    {
        var traveler = Traveler.Restore(
            "Rook", CharacterClass.Soldier, 1, 0, ClassDefinition.For(CharacterClass.Soldier).BaseStats,
            30, 30, 20, 20, 0, 1, 1, Coordinate.Origin, []);
        var weapon = Item.Create("Axe", ItemType.Weapon, 1, Rarity.Common);
        traveler.AddToInventory(weapon);
        traveler.Wield(weapon);

        Assert.Equal(weapon, traveler.EquippedWeapon);
    }

    [Fact]
    public void WardenDefeat_StartsFalseAndCanBeRecorded()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        Assert.False(traveler.HasDefeatedWarden(2));

        traveler.RecordWardenDefeat(2);

        Assert.True(traveler.HasDefeatedWarden(2));
        Assert.False(traveler.HasDefeatedWarden(3)); // per-level, not global
    }
}
