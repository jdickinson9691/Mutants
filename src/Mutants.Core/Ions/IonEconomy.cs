namespace Mutants.Core.Ions;

/// <summary>
/// Ion economy math per docs/GDD.md §2.1 ("Ion economy tuning (original)").
/// These specific formulas are already fixed in the GDD, so this is a
/// direct implementation rather than new tuning.
/// </summary>
public static class IonEconomy
{
    /// <summary>
    /// Converting an item destroys it for Ions equal to
    /// floor(base_item_value * 0.4), minimum 1 — docs/GDD.md §2.1.
    /// </summary>
    public static int ConvertValue(int baseItemValue)
    {
        if (baseItemValue < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseItemValue), baseItemValue,
                "Item value cannot be negative.");
        }

        return Math.Max(1, (int)Math.Floor(baseItemValue * 0.4));
    }

    /// <summary>Time travel costs 25 * target_level Ions — docs/GDD.md §3.2.</summary>
    public static int TimeTravelCost(int targetLevel)
    {
        if (targetLevel < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(targetLevel), targetLevel,
                "Target level must be at least 1.");
        }

        return 25 * targetLevel;
    }

    /// <summary>
    /// Passive drain is "1 Ion per N game-ticks, scaled slightly up at
    /// deeper time levels" (docs/GDD.md §2.1). The base interval and the
    /// exact per-level scaling are NOT specified in the GDD — this curve
    /// (10 ticks/Ion at level 1, tightening by 1 tick per level down to a
    /// floor of 3) is original placeholder tuning pending Design Agent
    /// sign-off, applied on top of the per-class <c>IonDrainMultiplier</c>
    /// in <see cref="Classes.ClassDefinition"/>.
    /// </summary>
    public static int TicksPerIonDrain(int timeLevel, double classDrainMultiplier)
    {
        if (timeLevel < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeLevel), timeLevel,
                "Time level must be at least 1.");
        }

        if (classDrainMultiplier <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(classDrainMultiplier), classDrainMultiplier,
                "Drain multiplier must be positive.");
        }

        var baseTicks = Math.Max(3, 10 - (timeLevel - 1));
        // A higher class drain multiplier means faster drain -> fewer ticks per Ion.
        return Math.Max(1, (int)Math.Round(baseTicks / classDrainMultiplier));
    }
}
