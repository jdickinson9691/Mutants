using ChronoTravelers.Core.Characters;
using ChronoTravelers.Core.Classes;
using ChronoTravelers.Core.Monsters;
using ChronoTravelers.Engine.Combat;

namespace ChronoTravelers.Engine.Tests.Combat;

public class CombatResolverTests
{
    // A fixed 0.5 roll makes the damage-variance factor exactly 1.0 (no
    // variance), so fights are deterministic; it also satisfies any
    // dropChance > 0.5 loot roll, since LootDropRoller "drops" when
    // roll < dropChance.
    private static StubRandomSource NeutralRandom() => StubRandomSource.Fixed(0.5);

    [Fact]
    public void Fight_TravelerDefeatsWeakMonster_AwardsXpAndLoot()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var monster = TestMonsters.FeralDog(); // tier 1: HP 28, attack 5, defense 2, xpReward 40, Torn Hide @ 0.7

        var result = CombatResolver.Fight(traveler, monster, NeutralRandom());

        Assert.True(result.TravelerWon);
        Assert.False(traveler.Health.IsDead);
        Assert.True(monster.Health.IsDead);
        Assert.Equal(monster.XpReward, result.XpAwarded);
        Assert.True(traveler.Xp >= monster.XpReward);
        Assert.NotEmpty(result.Log);
    }

    [Fact]
    public void Fight_TravelerDefeatsWeakMonster_LootIsAddedToInventory()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier);
        var monster = TestMonsters.FeralDog(); // Torn Hide drops at 0.7, roll is 0.5 -> drops

        var result = CombatResolver.Fight(traveler, monster, NeutralRandom());

        Assert.Single(result.ItemsDropped);
        Assert.Contains(result.ItemsDropped[0], traveler.Inventory);
    }

    [Fact]
    public void Fight_OverwhelmingMonster_TravelerLosesWithNoRewards()
    {
        var traveler = new Traveler("Rook", CharacterClass.Soldier); // 30 HP, defense 5
        var overlord = new Monster("Overlord", tier: 1, maxHp: 1_000_000, attackPower: 1000, defense: 0, speed: 100, xpReward: 500);

        var result = CombatResolver.Fight(traveler, overlord, NeutralRandom());

        Assert.False(result.TravelerWon);
        Assert.True(traveler.Health.IsDead);
        Assert.Equal(0, result.XpAwarded);
        Assert.Empty(result.ItemsDropped);
        Assert.Equal(0, traveler.Xp);
        Assert.Empty(traveler.Inventory);
    }

    [Fact]
    public void Fight_HigherSpeedActsFirst()
    {
        // A monster that one-shots the traveler, but only if it attacks first.
        var fastMonster = new Monster("Ambusher", tier: 1, maxHp: 10, attackPower: 1000, defense: 0, speed: 999, xpReward: 10);
        var traveler = new Traveler("Rook", CharacterClass.Soldier); // Agility 10 << monster speed 999

        var result = CombatResolver.Fight(traveler, fastMonster, NeutralRandom());

        Assert.False(result.TravelerWon);
        Assert.True(traveler.Health.IsDead);
        Assert.Equal(1, result.Rounds); // dead before ever getting a swing in
    }

    [Fact]
    public void Fight_LowerSpeedMonsterLetsTravelerActFirst()
    {
        var slowMonster = new Monster("Lumbering Husk", tier: 1, maxHp: 1, attackPower: 1000, defense: 0, speed: 1, xpReward: 10);
        var traveler = new Traveler("Rook", CharacterClass.Soldier); // Agility 10 > monster speed 1

        var result = CombatResolver.Fight(traveler, slowMonster, NeutralRandom());

        Assert.True(result.TravelerWon); // traveler's minimum-1 hit kills the 1-HP monster before it can swing
    }

    [Fact]
    public void Fight_MoreFavorableRandomRollsFinishFasterOrEqual()
    {
        var monster = () => Monster.Create("Punching Bag", tier: 1);

        var lowRollResult = CombatResolver.Fight(new Traveler("Rook", CharacterClass.Soldier), monster(), StubRandomSource.Fixed(0.0));
        var highRollResult = CombatResolver.Fight(new Traveler("Rook", CharacterClass.Soldier), monster(), StubRandomSource.Fixed(1.0));

        Assert.True(lowRollResult.TravelerWon);
        Assert.True(highRollResult.TravelerWon);
        Assert.True(highRollResult.Rounds <= lowRollResult.Rounds,
            "Higher damage-variance rolls should win in the same number of rounds or fewer.");
    }
}
