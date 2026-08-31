namespace ChronTravelers.Engine;

/// <summary>
/// Abstraction over randomness so combat/loot rolls stay deterministically
/// testable — tests supply a stub instead of depending on real RNG output.
/// </summary>
public interface IRandomSource
{
    /// <summary>Returns a value in [0, 1).</summary>
    double NextDouble();
}
