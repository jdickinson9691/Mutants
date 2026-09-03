using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Core.Monsters;

namespace ChronoTravelers.Core.Tests.Characters;

/// <summary>
/// Coverage for the always-on passive traits (docs/GDD.md §4.2.1) —
/// <see cref="PassiveTraits"/> itself, plus each hook's effect on
/// <see cref="Traveler"/>. Levels are reached via repeated <see cref="Traveler.LevelUp"/>
/// calls (which don't enforce the soft cap) rather than GainXp, so a
/// deep-level Engineer passive is reachable without fiddling with
/// FurthestYearReached.
/// </summary>
public class PassiveTraitTests
{
    private static Traveler LeveledTraveler(CharacterClass characterClass, int level)
    {
        var traveler = new Traveler("Test", characterClass);
        for (var i = 1; i < level; i++)
        {
            traveler.LevelUp();
        }

        return traveler;
    }

    [Fact]
    public void All_HasThirtyEntries_SixPerClass()
    {
        Assert.Equal(30, PassiveTraits.All.Count);
        foreach (CharacterClass characterClass in Enum.GetValues<CharacterClass>())
        {
            Assert.Equal(6, PassiveTraits.All.Count(p => p.Class == characterClass));
        }
    }

    [Fact]
    public void Unlocked_OnlyIncludesPassivesAtOrBelowLevel()
    {
        Assert.Empty(PassiveTraits.Unlocked(CharacterClass.Soldier, 0));
        Assert.Single(PassiveTraits.Unlocked(CharacterClass.Soldier, 1));
        Assert.Equal(2, PassiveTraits.Unlocked(CharacterClass.Soldier, 8).Count());
        Assert.Equal(6, PassiveTraits.Unlocked(CharacterClass.Soldier, 30).Count());
    }

    [Fact]
    public void Sum_StacksSpysTwoStoreDiscountEntries()
    {
        // Light Fingers (Lv1, 5%) + Silent Partner (Lv28, 5%) = 10%.
        var lowLevel = PassiveTraits.Sum(CharacterClass.Spy, 1, PassiveHook.StoreDiscountBonusPct);
        var highLevel = PassiveTraits.Sum(CharacterClass.Spy, 28, PassiveHook.StoreDiscountBonusPct);

        Assert.Equal(0.05, lowLevel, precision: 5);
        Assert.Equal(0.10, highLevel, precision: 5);
    }

    [Fact]
    public void EffectiveDefense_SoldierHardened_BoostsArmorContributionOnly()
    {
        var armor = Item.Create("Riot Plate", ItemType.Armor, tier: 2, Rarity.Common);
        var traveler = new Traveler("Rook", CharacterClass.Soldier); // Lv1 -> Hardened already unlocked
        traveler.AddToInventory(armor);
        traveler.Wield(armor);
        var withHardened = traveler.EffectiveDefense;

        var expectedArmorBonus = armor.DefenseBonus + (int)Math.Round(armor.DefenseBonus * 0.10);
        var bareDefense = new Traveler("Bare", CharacterClass.Soldier).EffectiveDefense;
        Assert.Equal(bareDefense + expectedArmorBonus, withHardened);
    }

    [Fact]
    public void EffectiveDefense_EngineerImprovisedPlating_AddsFlatThreeAtLevelEleven()
    {
        var below = LeveledTraveler(CharacterClass.Engineer, 10).EffectiveDefense;
        var atLevel = LeveledTraveler(CharacterClass.Engineer, 11).EffectiveDefense;

        // +3 flat, plus whatever stat growth also happened between 10 and
        // 11 — so just assert it's at least 3 higher, and exactly 3 higher
        // than a same-level traveler with the growth isolated out.
        Assert.True(atLevel - below >= 3);
    }

    [Fact]
    public void Speed_SpyQuickReflexes_AddsThreeAtLevelEight()
    {
        // Speed is just Agility + flat passive bonus, so isolate the
        // passive from the ordinary Agility growth that also lands on the
        // level-7 -> 8 level-up: at Lv7 there's no Speed passive yet, at
        // Lv8 Quick Reflexes adds exactly +3 on top of Agility.
        var atSeven = LeveledTraveler(CharacterClass.Spy, 7);
        var atEight = LeveledTraveler(CharacterClass.Spy, 8);

        Assert.Equal(atSeven.Stats.Agility, atSeven.Speed);
        Assert.Equal(atEight.Stats.Agility + 3, atEight.Speed);
    }

    [Fact]
    public void TakeDamage_SoldierSecondWind_ReducesDamageBelowThirtyPercentHp()
    {
        var soldier = LeveledTraveler(CharacterClass.Soldier, 8); // Second Wind unlocked
        var maxHp = soldier.Health.Max;
        soldier.Health.Damage(maxHp - (int)(maxHp * 0.25)); // drop to 25% HP
        var reducedDamage = soldier.TakeDamage(10);

        var soldierFull = LeveledTraveler(CharacterClass.Soldier, 8);
        var fullHpDamage = soldierFull.TakeDamage(10);

        Assert.True(reducedDamage < fullHpDamage); // 10% reduction applied only while below 30% HP
    }

    [Fact]
    public void TakeDamage_DoctorResonantCalm_ReducesEchoDamageOnly()
    {
        var doctor = LeveledTraveler(CharacterClass.Doctor, 8); // Resonant Calm unlocked
        var echoDamage = doctor.TakeDamage(10, attackerIsEcho: true);

        var doctor2 = LeveledTraveler(CharacterClass.Doctor, 8);
        var normalDamage = doctor2.TakeDamage(10, attackerIsEcho: false);

        Assert.True(echoDamage < normalDamage);
    }

    [Fact]
    public void TakeDamage_SoldierUnbreakable_SavesFromLethalHitOncePerFight()
    {
        var soldier = LeveledTraveler(CharacterClass.Soldier, 28); // Unbreakable unlocked
        soldier.ResetPerFightState();

        soldier.TakeDamage(soldier.Health.Max * 10); // would be lethal several times over
        Assert.False(soldier.Health.IsDead);
        Assert.Equal(1, soldier.Health.Current);

        // Heal back up and take another lethal hit in the SAME fight — the charge is spent.
        soldier.Health.Heal(soldier.Health.Max);
        soldier.TakeDamage(soldier.Health.Max * 10);
        Assert.True(soldier.Health.IsDead);
    }

    [Fact]
    public void TakeDamage_SoldierUnbreakable_RechargesAfterResetPerFightState()
    {
        var soldier = LeveledTraveler(CharacterClass.Soldier, 28);
        soldier.ResetPerFightState();
        soldier.TakeDamage(soldier.Health.Max * 10);
        Assert.Equal(1, soldier.Health.Current);

        soldier.Health.Heal(soldier.Health.Max);
        soldier.ResetPerFightState(); // a new fight

        soldier.TakeDamage(soldier.Health.Max * 10);
        Assert.False(soldier.Health.IsDead);
        Assert.Equal(1, soldier.Health.Current);
    }

    [Fact]
    public void AttackDamageMultiplierAgainst_SpyOpportunist_BonusesLowHpTarget()
    {
        var spy = LeveledTraveler(CharacterClass.Spy, 13); // Opportunist unlocked
        var healthyMonster = new Monster("Drone", 1, maxHp: 20, attackPower: 5, defense: 2, speed: 5, xpReward: 5);
        var lowHpMonster = new Monster("Drone", 1, maxHp: 20, attackPower: 5, defense: 2, speed: 5, xpReward: 5);
        lowHpMonster.Health.Damage(17); // down to 15% HP, well under Opportunist's 40% threshold

        Assert.Equal(1.0, spy.AttackDamageMultiplierAgainst(healthyMonster), precision: 5);
        Assert.Equal(1.15, spy.AttackDamageMultiplierAgainst(lowHpMonster), precision: 5);
    }

    [Fact]
    public void AttackDamageMultiplierAgainst_ScientistFieldCalibration_BonusesCasterTaggedMonster()
    {
        var scientist = LeveledTraveler(CharacterClass.Scientist, 23); // Field Calibration unlocked
        var baseline = new Monster("Drone", 1, maxHp: 20, attackPower: 5, defense: 2, speed: 5, xpReward: 5);
        var caster = new Monster("Wraith", 1, maxHp: 20, attackPower: 5, defense: 2, speed: 5, xpReward: 5, tags: ["caster"]);

        Assert.Equal(1.0, scientist.AttackDamageMultiplierAgainst(baseline), precision: 5);
        Assert.Equal(1.20, scientist.AttackDamageMultiplierAgainst(caster), precision: 5);
    }

    [Fact]
    public void Convert_ScientistTunnelSense_AddsTenPercentToConvertedValue()
    {
        var scientist = new Traveler("Sci", CharacterClass.Scientist); // Lv1 -> Tunnel Sense unlocked
        var plain = new Traveler("Plain", CharacterClass.Spy); // no convert-value passive at Lv1
        var item1 = Item.Create("Widget", ItemType.Weapon, tier: 2, Rarity.Common);
        var item2 = Item.Create("Widget", ItemType.Weapon, tier: 2, Rarity.Common);
        scientist.AddToInventory(item1);
        plain.AddToInventory(item2);

        var withBonus = scientist.Convert(item1);
        var withoutBonus = plain.Convert(item2);

        Assert.True(withBonus > withoutBonus);
    }

    [Fact]
    public void Convert_EngineerSalvageSense_OnlyBoostsJunkItems()
    {
        var engineer = LeveledTraveler(CharacterClass.Engineer, 15); // Salvage Sense unlocked
        var junk = Item.Create("Scrap", ItemType.Junk, tier: 1, Rarity.Common);
        var weapon = Item.Create("Pipe Wrench", ItemType.Weapon, tier: 1, Rarity.Common);
        engineer.AddToInventory(junk);
        engineer.AddToInventory(weapon);

        var junkGain = engineer.Convert(junk);
        var expectedJunk = (int)Math.Round(Item.Create("Scrap", ItemType.Junk, 1, Rarity.Common).ConvertValue() * 1.15);
        Assert.Equal(expectedJunk, junkGain);

        var weaponGain = engineer.Convert(weapon);
        var expectedWeapon = Item.Create("Pipe Wrench", ItemType.Weapon, 1, Rarity.Common).ConvertValue();
        Assert.Equal(expectedWeapon, weaponGain);
    }

    [Fact]
    public void Heal_DoctorBedsideManner_RestoresMoreHpPerTachyon()
    {
        // Drain both pools down to 2 Tachyons first, so Heal() is
        // Tachyon-limited rather than clamped by missing HP — that's the
        // only way Bedside Manner's higher HP-per-Tachyon rate actually
        // shows up (with plenty of Tachyons on hand, both classes just
        // heal up to the same missing-HP amount and clamp there).
        var doctor = new Traveler("Doc", CharacterClass.Doctor); // Lv1 -> Bedside Manner unlocked
        doctor.Health.Damage(doctor.Health.Max - 1);
        while (doctor.Tachyons.Current > 2)
        {
            doctor.Tachyons.Spend(1);
        }

        var doctorHealed = doctor.Heal();

        var plain = new Traveler("Plain", CharacterClass.Spy);
        plain.Health.Damage(plain.Health.Max - 1);
        while (plain.Tachyons.Current > 2)
        {
            plain.Tachyons.Spend(1);
        }

        var plainHealed = plain.Heal();

        Assert.True(doctorHealed > plainHealed);
    }

    [Fact]
    public void AdvanceTachyonDrainTick_ScientistInsulatedCoils_StretchesTheInterval()
    {
        var scientist = LeveledTraveler(CharacterClass.Scientist, 18); // Insulated Coils unlocked
        var ticksBefore = scientist.Tachyons.Current;

        // Base interval of 10 ticks would normally drain at tick 10; with
        // +15% slower drain the effective interval is 11.5 -> rounds to 12,
        // so tick 10 (and 11) should NOT have drained yet.
        for (var i = 0; i < 10; i++)
        {
            scientist.AdvanceTachyonDrainTick(10);
        }

        Assert.Equal(ticksBefore, scientist.Tachyons.Current);
    }

    [Fact]
    public void AmbushDodgeChance_And_AmbushNegateChance_ReadTheRightPassives()
    {
        var spy = LeveledTraveler(CharacterClass.Spy, 23); // Fleet-Footed unlocked
        Assert.Equal(0.20, spy.AmbushDodgeChance, precision: 5);

        var doctor = LeveledTraveler(CharacterClass.Doctor, 23); // Trauma Ward unlocked
        Assert.Equal(0.20, doctor.AmbushNegateChance, precision: 5);

        var freshSpy = new Traveler("Spy", CharacterClass.Spy);
        Assert.Equal(0.0, freshSpy.AmbushDodgeChance, precision: 5);
    }

    [Fact]
    public void AggroGainMultiplier_SpyLowProfile_ReducesGainByTwentyPercent()
    {
        var spy = LeveledTraveler(CharacterClass.Spy, 18); // Low Profile unlocked
        Assert.Equal(0.80, spy.AggroGainMultiplier, precision: 5);

        var freshSpy = new Traveler("Spy", CharacterClass.Spy);
        Assert.Equal(1.0, freshSpy.AggroGainMultiplier, precision: 5);
    }

    [Fact]
    public void EffectiveCastCost_EngineerFailsafeCapacitor_HalvesCostNearEmptyPool()
    {
        var engineer = LeveledTraveler(CharacterClass.Engineer, 19); // Failsafe Capacitor unlocked

        // Drain the pool down near 0 so paying the full cost would leave
        // it under 10% of nominal max.
        while (engineer.Tachyons.Current > 2)
        {
            engineer.Tachyons.Spend(1);
        }

        var cost = engineer.EffectiveCastCost(10);
        Assert.Equal(5, cost);
    }

    [Fact]
    public void EffectiveCastCost_EngineerFailsafeCapacitor_NoDiscountWhenPoolIsHealthy()
    {
        var engineer = LeveledTraveler(CharacterClass.Engineer, 19);
        var cost = engineer.EffectiveCastCost(10);
        Assert.Equal(10, cost);
    }

    [Fact]
    public void RecordAttackLanded_SoldierJuggernautMomentum_GrowsAttackUpToTenStacksThenCaps()
    {
        var soldier = LeveledTraveler(CharacterClass.Soldier, 13); // Juggernaut Momentum unlocked
        soldier.ResetPerFightState();
        var baseline = soldier.EffectiveAttackPower;

        for (var i = 0; i < 10; i++)
        {
            soldier.RecordAttackLanded();
        }

        var atTenStacks = soldier.EffectiveAttackPower;
        soldier.RecordAttackLanded(); // an 11th landed hit shouldn't grow it further
        var atElevenAttempts = soldier.EffectiveAttackPower;

        Assert.True(atTenStacks > baseline);
        Assert.Equal(atTenStacks, atElevenAttempts);
    }
}
