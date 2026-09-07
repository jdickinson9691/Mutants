using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Items;
using ChronoTravelers.Engine.Combat;

namespace ChronoTravelers.Engine.Tests.Combat;

/// <summary>docs/GDD.md §11's "player-vs-NPC-Traveler combat" — see <see cref="CombatResolver.FightTraveler"/>.</summary>
public class TravelerFightTests
{
    // A fixed 0.5 roll makes the damage-variance factor exactly 1.0 (no
    // variance), so fights are deterministic — same convention as
    // CombatResolverTests.
    private static StubRandomSource NeutralRandom() => StubRandomSource.Fixed(0.5);

    [Fact]
    public void FightTraveler_AttackerDefeatsWeakenedDefender_AwardsXpAndCredits()
    {
        var attacker = new Traveler("Rook", CharacterClass.Soldier);
        var defender = new Traveler("Ashen", CharacterClass.Scientist);
        defender.Health.Damage(defender.Health.Max - 1); // one hit from death

        var result = CombatResolver.FightTraveler(attacker, defender, NeutralRandom());

        Assert.True(result.AttackerWon);
        Assert.False(attacker.Health.IsDead);
        Assert.True(defender.Health.IsDead);
        Assert.True(result.XpAwarded > 0);
        Assert.True(result.CreditsAwarded > 0);
        Assert.Equal(result.XpAwarded, attacker.Xp);
        Assert.Equal(result.CreditsAwarded, attacker.Credits);
        Assert.NotEmpty(result.Log);
    }

    [Fact]
    public void FightTraveler_AttackerWins_DefendersWholeInventoryIsReturnedAsLoot()
    {
        var attacker = new Traveler("Rook", CharacterClass.Soldier);
        var defender = new Traveler("Ashen", CharacterClass.Scientist);
        defender.Health.Damage(defender.Health.Max - 1);

        var weapon = Item.Create("Rusty Blade", ItemType.Weapon, 1, Rarity.Common);
        var junk = Item.Create("Scrap Metal", ItemType.Junk, 1, Rarity.Common);
        defender.AddToInventory(weapon);
        defender.Wield(weapon);
        defender.AddToInventory(junk);

        var result = CombatResolver.FightTraveler(attacker, defender, NeutralRandom());

        Assert.True(result.AttackerWon);
        Assert.Contains(weapon, result.ItemsDropped);
        Assert.Contains(junk, result.ItemsDropped);
        Assert.Equal(2, result.ItemsDropped.Count);
        // The loot is only ever reported, never auto-added to the winner's
        // pack — the caller (console/Commands.cs) drops it on the ground,
        // same convention as a monster kill.
        Assert.DoesNotContain(weapon, attacker.Inventory);
    }

    [Fact]
    public void FightTraveler_OverwhelmedAttacker_LosesWithNoRewards()
    {
        var attacker = new Traveler("Rook", CharacterClass.Soldier);
        attacker.Health.Damage(attacker.Health.Max - 1); // one hit from death
        var defender = new Traveler("Ashen", CharacterClass.Scientist);

        var result = CombatResolver.FightTraveler(attacker, defender, NeutralRandom());

        Assert.False(result.AttackerWon);
        Assert.True(attacker.Health.IsDead);
        Assert.Equal(0, result.XpAwarded);
        Assert.Equal(0, result.CreditsAwarded);
        Assert.Empty(result.ItemsDropped);
        Assert.Equal(0, attacker.Xp);
        Assert.Equal(0, attacker.Credits);
    }

    [Fact]
    public void FightTraveler_LandingAndTakingHits_WearsDownEquippedWeaponAndArmor()
    {
        var attacker = new Traveler("Rook", CharacterClass.Soldier);
        var defender = new Traveler("Ashen", CharacterClass.Soldier);

        var weapon = Item.Create("Combat Knife", ItemType.Weapon, 3, Rarity.Common);
        var armor = Item.Create("Riot Vest", ItemType.Armor, 3, Rarity.Common);
        attacker.AddToInventory(weapon);
        attacker.Wield(weapon);
        defender.AddToInventory(armor);
        defender.Wield(armor);

        Assert.True(weapon.HasDurability);
        Assert.True(armor.HasDurability);
        var weaponDurabilityBefore = weapon.Durability;
        var armorDurabilityBefore = armor.Durability;

        CombatResolver.FightTraveler(attacker, defender, NeutralRandom());

        Assert.True(weapon.Durability < weaponDurabilityBefore);
        Assert.True(armor.Durability < armorDurabilityBefore);
    }

    [Fact]
    public void FightTraveler_ResetsPerFightStateForBothSides()
    {
        // Soldier's Juggernaut Momentum stacking (docs/GDD.md §4.2.1) reads
        // per-fight state that must start fresh each call — same guarantee
        // CombatResolver.Fight gives (see its own ResetPerFightState call).
        var attacker = new Traveler("Rook", CharacterClass.Soldier);
        var firstDefender = new Traveler("Ashen", CharacterClass.Scientist);
        firstDefender.Health.Damage(firstDefender.Health.Max - 1);
        CombatResolver.FightTraveler(attacker, firstDefender, NeutralRandom());

        var secondDefender = new Traveler("Vex", CharacterClass.Scientist);
        secondDefender.Health.Damage(secondDefender.Health.Max - 1);
        var result = CombatResolver.FightTraveler(attacker, secondDefender, NeutralRandom());

        Assert.True(result.AttackerWon);
        Assert.NotEmpty(result.Log);
    }
}
