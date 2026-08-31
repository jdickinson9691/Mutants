namespace ChronTravelers.Core.Ions;

/// <summary>
/// A character's current/max Ion pool. Ions below 0 are not allowed —
/// docs/GDD.md §2 says hitting 0 starts costing HP instead, which is a
/// combat/tick-loop concern handled by the caller, not by this pool.
/// </summary>
public sealed class IonPool
{
    public int Max { get; private set; }
    public int Current { get; private set; }

    public IonPool(int max, int? current = null)
    {
        if (max < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(max), max, "Max Ions cannot be negative.");
        }

        Max = max;
        Current = Math.Clamp(current ?? max, 0, max);
    }

    /// <summary>Raises the pool's cap (e.g. on level up), keeping Current unchanged.</summary>
    public void SetMax(int newMax)
    {
        if (newMax < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newMax), newMax, "Max Ions cannot be negative.");
        }

        Max = newMax;
        Current = Math.Min(Current, Max);
    }

    /// <summary>Adds Ions, clamped at Max. Returns the amount actually added.</summary>
    public int Add(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount cannot be negative.");
        }

        var before = Current;
        Current = Math.Min(Max, Current + amount);
        return Current - before;
    }

    /// <summary>True if at least <paramref name="amount"/> Ions are available to spend.</summary>
    public bool CanAfford(int amount) => amount >= 0 && Current >= amount;

    /// <summary>Spends Ions. Throws if the pool cannot afford it — callers should check CanAfford first.</summary>
    public void Spend(int amount)
    {
        if (!CanAfford(amount))
        {
            throw new InvalidOperationException(
                $"Cannot spend {amount} Ions with only {Current} available.");
        }

        Current -= amount;
    }
}
