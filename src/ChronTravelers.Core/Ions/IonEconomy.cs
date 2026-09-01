namespace ChronTravelers.Core.Ions;

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
    /// 3:1 is original tuning: healing still competes with travel/casting
    /// for the same Ion pool, but not so steeply that the early game
    /// becomes an unrecoverable attrition spiral (playtested).
    /// </summary>
    public const int HpPerIonHealed = 3;

    /// <summary>
    /// Ions spent per year of the timeline crossed by a <c>travel</c> —
    /// docs/GDD.md §3.2. Original tuning, lowered again (0.2 → 0.1 → 0.04)
    /// after playtesting: at 0.04 a one-tier early hop (~250 yrs) costs
    /// ~10 Ions — affordable from level 1, so mid-range travel is finally
    /// worth doing — while a full cross-timeline leap (3000 yrs) is still a
    /// ~120-Ion end-game commitment.
    /// </summary>
    public const double IonsPerYearTravelled = 0.04;

    /// <summary>
    /// Floor cost for <em>any</em> real jump, however short. At the 0.04
    /// linear rate a ~50-year hop is only ~2 Ions, so you could creep
    /// forward a decade at a time almost for free and farm every year on
    /// the way. A flat floor makes a short hop cost about the same as the
    /// ~200-year jump it should have been, so committing to a real jump is
    /// the efficient move (playtest feedback).
    /// </summary>
    public const int MinTravelCost = 8;

    /// <summary>
    /// Ion cost of travelling from <paramref name="fromYear"/> to
    /// <paramref name="toYear"/> — <c>ceil(<see cref="IonsPerYearTravelled"/>
    /// × |Δyear|)</c>, but never less than <see cref="MinTravelCost"/> for a
    /// real jump; 0 for staying put. Symmetric: retreating toward the
    /// present costs the same as advancing (this supersedes the old
    /// "retreat is free" rule now that travel is otherwise unrestricted).
    /// </summary>
    public static int TimeTravelCost(int fromYear, int toYear)
    {
        var distance = Math.Abs(toYear - fromYear);
        if (distance == 0)
        {
            return 0;
        }

        return Math.Max(MinTravelCost, (int)Math.Ceiling(IonsPerYearTravelled * distance));
    }

    /// <summary>
    /// Passive drain is "1 Ion per N game-ticks, scaled slightly up
    /// further into the future" (docs/GDD.md §2.1). The base interval and
    /// the exact scaling are NOT GDD-specified — this curve (10 ticks/Ion
    /// at tier 1, tightening by 1 tick per whole tier down to a floor of
    /// 3) is original placeholder tuning, applied on top of the per-class
    /// <c>IonDrainMultiplier</c> in <see cref="Classes.ClassDefinition"/>.
    /// <paramref name="scalingTier"/> is the whole-number difficulty tier
    /// for the current year (see ChronTravelers.Core.Time.TimeScale).
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

    /// <summary>
    /// Passive Ion <em>regen</em> out of combat — the counterpart to
    /// <see cref="TicksPerIonDrain"/> that keeps the early game
    /// recoverable. Adds 1 Ion every N ticks. Fast in the present
    /// (tier 1 ≈ 4 ticks/Ion) and slower further into the future
    /// (tier 9 ≈ 12), so early years net-<em>gain</em> Ions while the far
    /// future still net-drains them — matching docs/GDD.md §2.1's "later
    /// years are harsher survival environments". A higher class drain
    /// multiplier (Scientist/Engineer) also regens slower.
    /// </summary>
    public static int TicksPerIonRegen(int scalingTier, double classDrainMultiplier)
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

        var baseTicks = 3 + scalingTier;
        return Math.Max(1, (int)Math.Round(baseTicks * classDrainMultiplier));
    }
}
