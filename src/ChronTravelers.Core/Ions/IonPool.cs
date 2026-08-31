namespace ChronTravelers.Core.Ions;

/// <summary>
/// A character's Ion pool. Ions below 0 are not allowed — docs/GDD.md §2
/// says hitting 0 starts costing HP instead, which is a combat/tick-loop
/// concern handled by the caller, not by this pool.
///
/// The <b>player's</b> pool is <see cref="Uncapped"/>: an ordinary
/// <see cref="Add"/> (converting loot, an ability's Ion refund) never
/// clamps, so you can stockpile Ions past the nominal <see cref="Max"/>
/// for a long time-jump and no conversion is ever wasted. Passive regen
/// still respects the soft cap (<c>Add(amount, respectSoftCap: true)</c>)
/// so you can't just wait your way to an infinite pool. <see cref="Max"/>
/// is still tracked (it grows on level up and scales a couple of
/// abilities). Monster and NPC pools clamp everything.
/// </summary>
public sealed class IonPool
{
    public int Max { get; private set; }
    public int Current { get; private set; }

    /// <summary>When true, <see cref="Add"/> does not clamp and <see cref="Current"/> may exceed <see cref="Max"/>.</summary>
    public bool Uncapped { get; }

    public IonPool(int max, int? current = null, bool uncapped = false)
    {
        if (max < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(max), max, "Max Ions cannot be negative.");
        }

        Max = max;
        Uncapped = uncapped;
        var start = current ?? max;
        Current = uncapped ? Math.Max(0, start) : Math.Clamp(start, 0, max);
    }

    /// <summary>Raises the pool's nominal cap (e.g. on level up), keeping Current unchanged (never dragging an uncapped pool back down).</summary>
    public void SetMax(int newMax)
    {
        if (newMax < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newMax), newMax, "Max Ions cannot be negative.");
        }

        Max = newMax;
        if (!Uncapped)
        {
            Current = Math.Min(Current, Max);
        }
    }

    /// <summary>
    /// Adds Ions, returning the amount actually added. A capped pool — or
    /// an <see cref="Uncapped"/> pool topped up by passive regen
    /// (<paramref name="respectSoftCap"/>) — fills only up to <see cref="Max"/>
    /// and is never dragged <em>down</em> if it already sits above it.
    /// An ordinary add to an uncapped pool takes the whole amount.
    /// </summary>
    public int Add(int amount, bool respectSoftCap = false)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount cannot be negative.");
        }

        var before = Current;
        Current = Uncapped && !respectSoftCap
            ? Current + amount
            : Math.Max(Current, Math.Min(Max, Current + amount));
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
