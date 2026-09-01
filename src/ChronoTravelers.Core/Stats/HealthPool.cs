namespace ChronoTravelers.Core.Stats;

/// <summary>A character's current/max HP. Damage clamps at 0 (dead), never negative.</summary>
public sealed class HealthPool
{
    public int Max { get; private set; }
    public int Current { get; private set; }

    public bool IsDead => Current <= 0;

    public HealthPool(int max, int? current = null)
    {
        if (max < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(max), max, "Max HP must be at least 1.");
        }

        Max = max;
        Current = Math.Clamp(current ?? max, 0, max);
    }

    /// <summary>Raises the pool's cap (e.g. on level up), keeping Current unchanged.</summary>
    public void SetMax(int newMax)
    {
        if (newMax < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(newMax), newMax, "Max HP must be at least 1.");
        }

        Max = newMax;
        Current = Math.Min(Current, Max);
    }

    /// <summary>Applies damage, clamped so Current never drops below 0. Returns actual damage taken.</summary>
    public int Damage(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Damage cannot be negative.");
        }

        var before = Current;
        Current = Math.Max(0, Current - amount);
        return before - Current;
    }

    /// <summary>Heals, clamped at Max. Returns actual HP restored.</summary>
    public int Heal(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Heal amount cannot be negative.");
        }

        var before = Current;
        Current = Math.Min(Max, Current + amount);
        return Current - before;
    }
}
