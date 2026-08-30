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
}
