namespace ChronTravelers.Engine;

/// <summary>Real-randomness <see cref="IRandomSource"/>, backed by <see cref="System.Random"/>. Use in production; use a stub/seeded source in tests.</summary>
public sealed class SystemRandomSource(Random? random = null) : IRandomSource
{
    private readonly Random _random = random ?? Random.Shared;

    public double NextDouble() => _random.NextDouble();
}
