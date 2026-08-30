namespace Mutants.Core.Monsters;

/// <summary>
/// Tier-to-stat baselines for monsters. docs/GDD.md §5 confirms loot
/// scales with the time-travel level a monster is native to, but gives no
/// monster stat formulas — these curves are original tuning pending
/// Design Agent sign-off, kept roughly in scale with
/// <see cref="Items.LootScaling"/> and <see cref="Stats.Leveling"/> so a
/// tier-N monster is a sensible fight for a character around level
/// <c>10 * N</c> (the soft level cap for that time-travel level).
/// </summary>
public static class MonsterScaling
{
    public static int BaseHp(int tier) => Require(tier, 20 + 8 * tier);

    public static int BaseAttackPower(int tier) => Require(tier, 3 + 2 * tier);

    public static int BaseDefense(int tier) => Require(tier, 1 + tier);

    public static int BaseSpeed(int tier) => Require(tier, 8 + tier);

    /// <summary>XP reward for defeating a tier-N monster — deliberately generous relative to <see cref="Stats.Leveling.CumulativeXpForLevel"/> so a handful of kills advances a level.</summary>
    public static int XpReward(int tier) => Require(tier, 40 * tier);

    private static int Require(int tier, int value)
    {
        if (tier < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "Tier must be at least 1.");
        }

        return value;
    }
}
