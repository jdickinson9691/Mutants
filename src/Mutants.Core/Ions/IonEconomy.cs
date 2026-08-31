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

    /// <summary>
    /// HP restored per Ion spent via the "heal" command — docs/GDD.md §2
    /// [SOURCE] confirms "spend Ions to heal wounds directly," usable at
    /// any time, but no specific ratio survives in the historical record.
    /// 1:1 is original tuning, deliberately steep (not a cheap top-off) so
    /// healing genuinely competes with travel/casting/survival for the
    /// same Ion pool, matching Ions' "single unified resource" design
    /// intent rather than making it a trivial no-cost habit.
    /// </summary>
    public const int HpPerIonHealed = 1;

    /// <summary>Ions spent per year of the timeline crossed by a <c>travel</c> — docs/GDD.md §3.2. Original tuning.</summary>
    public const double IonsPerYearTravelled = 0.2;

    /// <summary>
    /// Ion cost of travelling from <paramref name="fromYear"/> to
    /// <paramref name="toYear"/> — <c>ceil(0.2 × |Δyear|)</c>, minimum 1
    /// for any real jump, 0 for staying put. Symmetric: retreating toward
    /// the present costs the same as advancing (this supersedes the old
    /// "retreat is free" rule now that travel is otherwise unrestricted).
    /// </summary>
    public static int TimeTravelCost(int fromYear, int toYear)
    {
        var distance = Math.Abs(toYear - fromYear);
        if (distance == 0)
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(IonsPerYearTravelled * distance));
    }

    /// <summary>
    /// Passive drain is "1 Ion per N game-ticks, scaled slightly up
    /// further into the future" (docs/GDD.md §2.1). The base interval and
    /// the exact scaling are NOT GDD-specified — this curve (10 ticks/Ion
    /// at tier 1, tightening by 1 tick per whole tier down to a floor of
    /// 3) is original placeholder tuning, applied on top of the per-class
    /// <c>IonDrainMultiplier</c> in <see cref="Classes.ClassDefinition"/>.
    /// <paramref name="scalingTier"/> is the whole-number difficulty tier
    /// for the current year (see Mutants.Core.Time.TimeScale).
    /// </summary>
    public static int TicksPerIonDrain(int scalingTier, double classDrainMultiplier)
    {
        if (scalingTier < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(scalingTier), scalingTier,
                "Scaling tier must be at least 1.");
        }

        if (classDrainMultiplier <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(classDrainMultiplier), classDrainMultiplier,
                "Drain multiplier must be positive.");
        }

        var baseTicks = Math.Max(3, 10 - (scalingTier - 1));
        // A higher class drain multiplier means faster drain -> fewer ticks per Ion.
        return Math.Max(1, (int)Math.Round(baseTicks / classDrainMultiplier));
    }
}
