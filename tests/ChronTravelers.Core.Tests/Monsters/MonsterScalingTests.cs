using ChronTravelers.Core.Monsters;

namespace ChronTravelers.Core.Tests.Monsters;

public class MonsterScalingTests
{
    [Fact]
    public void AllBaselines_IncreaseWithTier()
    {
        Assert.True(MonsterScaling.BaseHp(5) > MonsterScaling.BaseHp(1));
        Assert.True(MonsterScaling.BaseAttackPower(5) > MonsterScaling.BaseAttackPower(1));
        Assert.True(MonsterScaling.BaseDefense(5) > MonsterScaling.BaseDefense(1));
        Assert.True(MonsterScaling.BaseSpeed(5) > MonsterScaling.BaseSpeed(1));
        Assert.True(MonsterScaling.XpReward(5) > MonsterScaling.XpReward(1));
        Assert.True(MonsterScaling.BaseIons(5) > MonsterScaling.BaseIons(1));
    }

    [Fact]
    public void BaseIons_IsSmallerThanBaseHp_AndHasBothOverloads()
    {
        Assert.True(MonsterScaling.BaseIons(3) < MonsterScaling.BaseHp(3), "A monster's Ion pool should be well under its HP.");
        Assert.Equal(MonsterScaling.BaseIons(4), (int)System.Math.Round(MonsterScaling.BaseIons(4.0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => MonsterScaling.BaseIons(0.5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AllBaselines_RejectTierBelowOne(int tier)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MonsterScaling.BaseHp(tier));
        Assert.Throws<ArgumentOutOfRangeException>(() => MonsterScaling.BaseAttackPower(tier));
        Assert.Throws<ArgumentOutOfRangeException>(() => MonsterScaling.BaseDefense(tier));
        Assert.Throws<ArgumentOutOfRangeException>(() => MonsterScaling.BaseSpeed(tier));
        Assert.Throws<ArgumentOutOfRangeException>(() => MonsterScaling.XpReward(tier));
    }

    [Fact]
    public void IntOverload_MatchesDoubleOverloadRoundedAtWholeTiers()
    {
        for (var tier = 1; tier <= 9; tier++)
        {
            Assert.Equal(MonsterScaling.BaseHp(tier), (int)System.Math.Round(MonsterScaling.BaseHp((double)tier)));
            Assert.Equal(MonsterScaling.BaseAttackPower(tier), (int)System.Math.Round(MonsterScaling.BaseAttackPower((double)tier)));
            Assert.Equal(MonsterScaling.XpReward(tier), (int)System.Math.Round(MonsterScaling.XpReward((double)tier)));
        }
    }

    [Fact]
    public void DoubleOverload_IsContinuousBetweenWholeTiers()
    {
        var atTwo = MonsterScaling.BaseHp(2.0);
        var atHalf = MonsterScaling.BaseHp(2.5);
        var atThree = MonsterScaling.BaseHp(3.0);

        Assert.True(atHalf > atTwo && atHalf < atThree);
        Assert.Equal((atTwo + atThree) / 2, atHalf, precision: 6);
    }

    [Fact]
    public void DoubleOverload_RejectsTierBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MonsterScaling.BaseHp(0.9));
        Assert.Throws<ArgumentOutOfRangeException>(() => MonsterScaling.XpReward(0.0));
    }

    [Theory]
    [InlineData(1, 1, 100)]   // level 1 vs tier 1 — well within the band, full XP
    [InlineData(1, 10, 100)]  // exactly at the tier-1 band cap — still full
    [InlineData(1, 15, 60)]   // 5 levels past the cap — 8%/level off
    [InlineData(1, 20, 20)]   // 10 past
    [InlineData(1, 25, 10)]   // hit the 10% floor
    [InlineData(1, 40, 10)]   // never below the floor
    [InlineData(5, 40, 100)]  // level 40 vs tier 5 (cap 50) — still in-band, full
    [InlineData(5, 60, 20)]   // 10 past the tier-5 cap
    public void KillXp_FullWithinTheBand_ThenFallsOffPastTheCapToAFloor(int tier, int killerLevel, int expected)
    {
        Assert.Equal(expected, MonsterScaling.KillXp(baseXp: 100, monsterTier: tier, killerLevel: killerLevel));
    }

    [Fact]
    public void KillXp_NeverReturnsZeroForARealReward()
    {
        Assert.Equal(1, MonsterScaling.KillXp(baseXp: 4, monsterTier: 1, killerLevel: 99));
    }
}
