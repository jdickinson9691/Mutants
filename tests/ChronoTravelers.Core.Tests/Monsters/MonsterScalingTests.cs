using ChronoTravelers.Core.Monsters;

namespace ChronoTravelers.Core.Tests.Monsters;

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
        Assert.True(MonsterScaling.BaseTachyons(5) > MonsterScaling.BaseTachyons(1));
    }

    [Fact]
    public void BaseAttackPower_IsSuperlinear_RampingHarderAtHighTiers()
    {
        var lowStep = MonsterScaling.BaseAttackPower(2) - MonsterScaling.BaseAttackPower(1);
        var highStep = MonsterScaling.BaseAttackPower(9) - MonsterScaling.BaseAttackPower(8);

        Assert.True(highStep > lowStep * 2,
            $"a tier-8→9 step ({highStep}) should dwarf a tier-1→2 step ({lowStep})");
        // still tame at the low end (near the old 3 + 2·tier)
        Assert.True(MonsterScaling.BaseAttackPower(1) < 6);
    }

    [Fact]
    public void BaseTachyons_IsSmallerThanBaseHp_AndHasBothOverloads()
    {
        Assert.True(MonsterScaling.BaseTachyons(3) < MonsterScaling.BaseHp(3), "A monster's Tachyon pool should be well under its HP.");
        Assert.Equal(MonsterScaling.BaseTachyons(4), (int)System.Math.Round(MonsterScaling.BaseTachyons(4.0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => MonsterScaling.BaseTachyons(0.5));
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
        // BaseDefense is linear, so a half tier is exactly the midpoint.
        var defTwo = MonsterScaling.BaseDefense(2.0);
        var defHalf = MonsterScaling.BaseDefense(2.5);
        var defThree = MonsterScaling.BaseDefense(3.0);
        Assert.True(defHalf > defTwo && defHalf < defThree);
        Assert.Equal((defTwo + defThree) / 2, defHalf, precision: 6);

        // BaseHp / BaseAttackPower are superlinear (convex): still smoothly
        // increasing between whole tiers, but a half tier sits *below* the
        // straight-line midpoint.
        var convex = new Func<double, double>[]
        {
            t => MonsterScaling.BaseHp(t),
            t => MonsterScaling.BaseAttackPower(t),
        };
        foreach (var f in convex)
        {
            double lo = f(2.0), mid = f(2.5), hi = f(3.0);
            Assert.True(mid > lo && mid < hi, "still monotonically increasing between tiers");
            Assert.True(mid < (lo + hi) / 2, "convex — a half tier is below the linear midpoint");
        }
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
