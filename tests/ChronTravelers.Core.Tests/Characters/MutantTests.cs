using ChronTravelers.Core.Characters;
using ChronTravelers.Core.Classes;
using ChronTravelers.Core.Items;
using ChronTravelers.Core.Stats;
using ChronTravelers.Core.World;

namespace ChronTravelers.Core.Tests.Characters;

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
    public void GainXp_StopsAtSoftLevelCapForFurthestYearReached()
    {
        // Year 2000 -> soft cap of character level 10.
        var mutant = new Mutant("Rook", CharacterClass.Priest, startingYear: 2000);

        mutant.GainXp(Leveling.CumulativeXpForLevel(Leveling.MaxCharacterLevel));

        Assert.Equal(10, mutant.Level);
    }

    [Fact]
    public void SetCurrentYear_RaisesFurthestYearReachedButNeverRegressesIt()
    {
        var mutant = new Mutant("Rook", CharacterClass.Priest, startingYear: 2000);

        mutant.SetCurrentYear(3125);
        Assert.Equal(3125, mutant.CurrentYear);
        Assert.Equal(3125, mutant.FurthestYearReached);

        mutant.SetCurrentYear(2100); // retreat: current moves, furthest doesn't
        Assert.Equal(2100, mutant.CurrentYear);
        Assert.Equal(3125, mutant.FurthestYearReached);
    }

    [Fact]
    public void Convert_RemovesItemFromInventoryAndAddsIons()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        mutant.Ions.Spend(mutant.Ions.Current); // drain to 0 so Add() has headroom to observe
        var item = Item.Create("Scrap Metal", ItemType.Junk, tier: 1, Rarity.Common); // value 22
        mutant.AddToInventory(item);

        var gained = mutant.Convert(item);

        Assert.Equal(8, gained); // floor(22 * 0.4) = 8
        Assert.DoesNotContain(item, mutant.Inventory);
        Assert.Equal(8, mutant.Ions.Current);
    }

    [Fact]
    public void Convert_ThrowsIfItemNotInInventory()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var item = Item.Create("Ghost Item", ItemType.Junk, 1, Rarity.Common);

        Assert.Throws<InvalidOperationException>(() => mutant.Convert(item));
    }

    [Fact]
    public void Consume_HealEffect_RemovesItemAndRestoresFlatHp()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        mutant.Health.Damage(15);
        var ration = Item.Create("Ration Pack", ItemType.Consumable, 1, Rarity.Common, consumableEffect: ConsumableEffectType.Heal, effectMagnitude: 10);
        mutant.AddToInventory(ration);

        var healed = mutant.Consume(ration);

        Assert.Equal(10, healed);
        Assert.DoesNotContain(ration, mutant.Inventory);
        Assert.Equal(mutant.Health.Max - 5, mutant.Health.Current);
    }

    [Fact]
    public void Consume_HealEffect_NeverOverheals()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        mutant.Health.Damage(2);
        var ration = Item.Create("Ration Pack", ItemType.Consumable, 1, Rarity.Common, consumableEffect: ConsumableEffectType.Heal, effectMagnitude: 50);
        mutant.AddToInventory(ration);

        var healed = mutant.Consume(ration);

        Assert.Equal(2, healed); // only the 2 missing HP, not the full 50
        Assert.Equal(mutant.Health.Max, mutant.Health.Current);
    }

    [Fact]
    public void Consume_BuffAttackEffect_AddsATimedActiveEffectAndBoostsEffectiveAttack()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var attackBefore = mutant.EffectiveAttackPower;
        var potion = Item.Create("Adrenal Stim", ItemType.Consumable, 2, Rarity.Uncommon, consumableEffect: ConsumableEffectType.BuffAttack, effectMagnitude: 4, effectDurationTicks: 15);
        mutant.AddToInventory(potion);

        mutant.Consume(potion);

        Assert.Equal(attackBefore + 4, mutant.EffectiveAttackPower);
        Assert.Single(mutant.ActiveEffects);
        Assert.Equal(15, mutant.ActiveEffects[0].TicksRemaining);
    }

    [Fact]
    public void Consume_BuffDefenseEffect_BoostsEffectiveDefense()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var defenseBefore = mutant.EffectiveDefense;
        var potion = Item.Create("Patch Kit", ItemType.Consumable, 1, Rarity.Common, consumableEffect: ConsumableEffectType.BuffDefense, effectMagnitude: 3, effectDurationTicks: 15);
        mutant.AddToInventory(potion);

        mutant.Consume(potion);

        Assert.Equal(defenseBefore + 3, mutant.EffectiveDefense);
    }

    [Fact]
    public void Consume_ThrowsForAnItemThatIsNotUsable()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var junk = Item.Create("Scrap Metal", ItemType.Junk, 1, Rarity.Common);
        mutant.AddToInventory(junk);

        Assert.Throws<InvalidOperationException>(() => mutant.Consume(junk));
        Assert.Contains(junk, mutant.Inventory); // untouched - nothing consumed
    }

    [Fact]
    public void AdvanceEffectTicks_CountsDownAndRemovesExpiredEffects()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var potion = Item.Create("Adrenal Stim", ItemType.Consumable, 2, Rarity.Uncommon, consumableEffect: ConsumableEffectType.BuffAttack, effectMagnitude: 4, effectDurationTicks: 2);
        mutant.AddToInventory(potion);
        mutant.Consume(potion);
        var attackWhileActive = mutant.EffectiveAttackPower;

        mutant.AdvanceEffectTicks();
        Assert.Single(mutant.ActiveEffects);
        Assert.Equal(1, mutant.ActiveEffects[0].TicksRemaining);
        Assert.Equal(attackWhileActive, mutant.EffectiveAttackPower); // still active

        mutant.AdvanceEffectTicks();
        Assert.Empty(mutant.ActiveEffects);
        Assert.True(mutant.EffectiveAttackPower < attackWhileActive); // expired
    }

    [Fact]
    public void Heal_AlreadyAtFullHealth_DoesNothingAndSpendsNoIons()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);

        var healed = mutant.Heal();

        Assert.Equal(0, healed);
        Assert.Equal(mutant.Ions.Max, mutant.Ions.Current);
    }

    [Fact]
    public void Heal_SpendsIonsAtTheHealRatioAndFullyRecoversWhenAffordable()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        mutant.Health.Damage(9); // exactly 3 Ions' worth at 3:1
        var ionsBefore = mutant.Ions.Current;

        var healed = mutant.Heal();

        Assert.Equal(9, healed);
        Assert.Equal(mutant.Health.Max, mutant.Health.Current);
        Assert.Equal(ionsBefore - 3, mutant.Ions.Current);
    }

    [Fact]
    public void Heal_NotEnoughIonsToFullyHeal_HealsOnlyAsMuchAsAffordable()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        mutant.Health.Damage(25);
        mutant.Ions.Spend(mutant.Ions.Current - 2); // leave exactly 2 Ions -> 6 HP at 3:1

        var healed = mutant.Heal();

        Assert.Equal(6, healed);
        Assert.Equal(mutant.Health.Max - 19, mutant.Health.Current); // healed 6 of the 25 missing
        Assert.Equal(0, mutant.Ions.Current);
    }

    [Fact]
    public void Heal_NoIonsAvailable_DoesNothing()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        mutant.Health.Damage(10);
        mutant.Ions.Spend(mutant.Ions.Current);

        var healed = mutant.Heal();

        Assert.Equal(0, healed);
        Assert.Equal(mutant.Health.Max - 10, mutant.Health.Current);
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
    public void Wield_RoutesARangedWeaponToItsOwnSlot_AndUnequipsOnRemove()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var melee = Item.Create("Axe", ItemType.Weapon, 1, Rarity.Common);
        var bow = Item.CreateRanged("Longbow", 1, Rarity.Uncommon, RangedKind.Bow, ammoCapacity: 10);
        mutant.AddToInventory(melee);
        mutant.AddToInventory(bow);

        mutant.Wield(melee);
        mutant.Wield(bow);

        Assert.Equal(melee, mutant.EquippedWeapon);
        Assert.Equal(bow, mutant.EquippedRanged);
        Assert.Null(mutant.EquippedArmor);

        mutant.RemoveFromInventory(bow);
        Assert.Null(mutant.EquippedRanged);
        Assert.Equal(melee, mutant.EquippedWeapon); // untouched
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

    [Fact]
    public void Sell_RemovesItemAndAddsRiblets()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var item = Item.Create("Scrap Metal", ItemType.Junk, tier: 2, Rarity.Common); // value 34
        mutant.AddToInventory(item);

        var gained = mutant.Sell(item);

        Assert.Equal(34, gained);
        Assert.DoesNotContain(item, mutant.Inventory);
        Assert.Equal(34, mutant.Riblets);
    }

    [Fact]
    public void Sell_WithExplicitPrice_OverridesTheFlatRate()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var item = Item.Create("Scrap Metal", ItemType.Junk, tier: 2, Rarity.Common); // flat value 20
        mutant.AddToInventory(item);

        var gained = mutant.Sell(item, riblets: 7);

        Assert.Equal(7, gained);
        Assert.Equal(7, mutant.Riblets);
    }

    [Fact]
    public void RemoveFromInventory_RemovesWithNoPayout()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var startingIons = mutant.Ions.Current;
        var item = Item.Create("Scrap Metal", ItemType.Junk, tier: 2, Rarity.Common);
        mutant.AddToInventory(item);

        mutant.RemoveFromInventory(item);

        Assert.DoesNotContain(item, mutant.Inventory);
        Assert.Equal(0, mutant.Riblets);
        Assert.Equal(startingIons, mutant.Ions.Current); // no side effect on Ions either
    }

    [Fact]
    public void RemoveFromInventory_UnequipsIfWielded()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var weapon = Item.Create("Axe", ItemType.Weapon, 1, Rarity.Common);
        mutant.AddToInventory(weapon);
        mutant.Wield(weapon);

        mutant.RemoveFromInventory(weapon);

        Assert.Null(mutant.EquippedWeapon);
    }

    [Fact]
    public void Sell_ThrowsIfItemNotInInventory()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var item = Item.Create("Ghost Item", ItemType.Junk, 1, Rarity.Common);

        Assert.Throws<InvalidOperationException>(() => mutant.Sell(item));
    }

    [Fact]
    public void Convert_UnequipsTheItemIfItWasWielded()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var weapon = Item.Create("Axe", ItemType.Weapon, 1, Rarity.Common);
        mutant.AddToInventory(weapon);
        mutant.Wield(weapon);

        mutant.Convert(weapon);

        Assert.Null(mutant.EquippedWeapon);
    }

    [Fact]
    public void Sell_UnequipsTheItemIfItWasWielded()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var armor = Item.Create("Plate", ItemType.Armor, 1, Rarity.Common);
        mutant.AddToInventory(armor);
        mutant.Wield(armor);

        mutant.Sell(armor);

        Assert.Null(mutant.EquippedArmor);
    }

    [Fact]
    public void EffectiveAttackPower_IsPrimaryStatWhenUnarmed()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        Assert.Equal(mutant.Stats.Strength, mutant.EffectiveAttackPower);
    }

    [Fact]
    public void EffectiveAttackPower_AddsFullWeaponBonusForClassCompatibleGear()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var weapon = Item.Create("Axe", ItemType.Weapon, 3, Rarity.Common, CharacterClass.Warrior);
        mutant.AddToInventory(weapon);
        mutant.Wield(weapon);

        Assert.Equal(mutant.Stats.Strength + weapon.AttackBonus, mutant.EffectiveAttackPower);
    }

    [Fact]
    public void EffectiveAttackPower_PenalizesOffClassWeapon()
    {
        var mage = new Mutant("Zeta", CharacterClass.Mage);
        var warriorAxe = Item.Create("Great Axe", ItemType.Weapon, 3, Rarity.Common, CharacterClass.Warrior);
        mage.AddToInventory(warriorAxe);
        mage.Wield(warriorAxe);

        var fullBonusAttack = mage.Stats.Get(mage.ClassDefinition.PrimaryStat) + warriorAxe.AttackBonus;
        Assert.True(mage.EffectiveAttackPower < fullBonusAttack);
    }

    [Fact]
    public void EffectiveDefense_AddsArmorBonus()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var unarmoredDefense = mutant.EffectiveDefense;

        var armor = Item.Create("Plate", ItemType.Armor, 3, Rarity.Common, CharacterClass.Warrior);
        mutant.AddToInventory(armor);
        mutant.Wield(armor);

        Assert.Equal(unarmoredDefense + armor.DefenseBonus, mutant.EffectiveDefense);
    }

    [Fact]
    public void AdvanceIonDrainTick_DoesNothingBeforeTheInterval()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var startingIons = mutant.Ions.Current;

        var hpLost = mutant.AdvanceIonDrainTick(ticksPerDrain: 5);

        Assert.False(hpLost);
        Assert.Equal(startingIons, mutant.Ions.Current);
    }

    [Fact]
    public void AdvanceIonDrainTick_SpendsOneIonOnceIntervalElapses()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        var startingIons = mutant.Ions.Current;

        mutant.AdvanceIonDrainTick(ticksPerDrain: 3);
        mutant.AdvanceIonDrainTick(ticksPerDrain: 3);
        var hpLost = mutant.AdvanceIonDrainTick(ticksPerDrain: 3);

        Assert.False(hpLost);
        Assert.Equal(startingIons - 1, mutant.Ions.Current);
    }

    [Fact]
    public void AdvanceIonDrainTick_DamagesHealthWhenOutOfIons()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        mutant.Ions.Spend(mutant.Ions.Current); // drain to 0
        var startingHp = mutant.Health.Current;

        var hpLost = mutant.AdvanceIonDrainTick(ticksPerDrain: 1);

        Assert.True(hpLost);
        Assert.Equal(startingHp - 1, mutant.Health.Current);
    }

    [Fact]
    public void AdvanceIonDrainTick_RejectsNonPositiveInterval()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        Assert.Throws<ArgumentOutOfRangeException>(() => mutant.AdvanceIonDrainTick(0));
    }

    [Fact]
    public void AdvanceIonRegenTick_AddsOneIonOnceIntervalElapses()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        mutant.Ions.Spend(mutant.Ions.Current); // drain to 0

        Assert.False(mutant.AdvanceIonRegenTick(ticksPerRegen: 3));
        Assert.False(mutant.AdvanceIonRegenTick(ticksPerRegen: 3));
        var added = mutant.AdvanceIonRegenTick(ticksPerRegen: 3);

        Assert.True(added);
        Assert.Equal(1, mutant.Ions.Current);
    }

    [Fact]
    public void AdvanceIonRegenTick_ReportsNothingAddedWhenAlreadyAtMax()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior); // starts at full Ions

        var added = mutant.AdvanceIonRegenTick(ticksPerRegen: 1);

        Assert.False(added);
        Assert.Equal(mutant.Ions.Max, mutant.Ions.Current);
    }

    [Fact]
    public void AdvanceIonRegenTick_RejectsNonPositiveInterval()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        Assert.Throws<ArgumentOutOfRangeException>(() => mutant.AdvanceIonRegenTick(0));
    }

    [Fact]
    public void CurrentYear_DefaultsToTheStartOfTheTimeline()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        Assert.Equal(2000, mutant.CurrentYear);
        Assert.Equal(2000, mutant.FurthestYearReached);
    }

    [Fact]
    public void SetCurrentYear_ClampsOutOfRangeYearsToTheTimeline()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        mutant.SetCurrentYear(9999);
        Assert.Equal(5000, mutant.CurrentYear);

        mutant.SetCurrentYear(1000);
        Assert.Equal(2000, mutant.CurrentYear);
    }

    [Fact]
    public void Constructor_RejectsAStartingYearOffTheTimeline()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Mutant("Rook", CharacterClass.Warrior, startingYear: 1900));
    }

    [Fact]
    public void Restore_ReconstructsFullStateExactly()
    {
        var stats = new StatBlock(20, 15, 10, 12);
        var mutant = Mutant.Restore(
            "Rook", CharacterClass.Warrior, level: 7, xp: 555, stats,
            currentHp: 40, maxHp: 60, currentIons: 5, maxIons: 30, riblets: 250,
            currentYear: 2900, furthestYearReached: 3200, position: new Coordinate(2, -1),
            defeatedGatekeeperYears: [2412, 3187]);

        Assert.Equal("Rook", mutant.Name);
        Assert.Equal(CharacterClass.Warrior, mutant.Class);
        Assert.Equal(7, mutant.Level);
        Assert.Equal(555, mutant.Xp);
        Assert.Equal(stats, mutant.Stats);
        Assert.Equal(40, mutant.Health.Current);
        Assert.Equal(60, mutant.Health.Max);
        Assert.Equal(5, mutant.Ions.Current);
        Assert.Equal(30, mutant.Ions.Max);
        Assert.Equal(250, mutant.Riblets);
        Assert.Equal(3200, mutant.FurthestYearReached);
        Assert.Equal(2900, mutant.CurrentYear);
        Assert.Equal(new Coordinate(2, -1), mutant.Position);
        Assert.True(mutant.HasDefeatedGatekeeper(2412));
        Assert.True(mutant.HasDefeatedGatekeeper(3187));
        Assert.False(mutant.HasDefeatedGatekeeper(4000));
        Assert.Empty(mutant.Inventory);
        Assert.Null(mutant.EquippedWeapon);
    }

    [Fact]
    public void Restore_RejectsEmptyName()
    {
        Assert.Throws<ArgumentException>(() => Mutant.Restore(
            "", CharacterClass.Warrior, 1, 0, new StatBlock(10, 10, 10, 10),
            30, 30, 20, 20, 0, 1, 1, Coordinate.Origin, []));
    }

    [Fact]
    public void Restore_ThenAddToInventoryAndWield_WorksNormally()
    {
        var mutant = Mutant.Restore(
            "Rook", CharacterClass.Warrior, 1, 0, ClassDefinition.For(CharacterClass.Warrior).BaseStats,
            30, 30, 20, 20, 0, 1, 1, Coordinate.Origin, []);
        var weapon = Item.Create("Axe", ItemType.Weapon, 1, Rarity.Common);
        mutant.AddToInventory(weapon);
        mutant.Wield(weapon);

        Assert.Equal(weapon, mutant.EquippedWeapon);
    }

    [Fact]
    public void GatekeeperDefeat_StartsFalseAndCanBeRecorded()
    {
        var mutant = new Mutant("Rook", CharacterClass.Warrior);
        Assert.False(mutant.HasDefeatedGatekeeper(2));

        mutant.RecordGatekeeperDefeat(2);

        Assert.True(mutant.HasDefeatedGatekeeper(2));
        Assert.False(mutant.HasDefeatedGatekeeper(3)); // per-level, not global
    }
}
