using Mutants.Core.Monsters;

namespace Mutants.Core.Tests.Monsters;

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
}
