namespace ChronoTravelers.Core.Time;

/// <summary>
/// Builds <see cref="Random"/> instances seeded from a world seed plus a
/// per-purpose salt, so every deterministic slice of world generation
/// (a year's map, its stores, a Warden's name) draws from its own
/// stable stream. Same inputs → same stream, on any machine and across
/// process restarts — which is why this rolls its own FNV-1a-style mix
/// instead of <see cref="System.HashCode"/> (whose seed is randomized
/// per process).
/// </summary>
public static class DeterministicRandom
{
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    public static Random For(long worldSeed, int year, string purpose)
    {
        var h = FnvOffset;
        h = Mix(h, (ulong)worldSeed);
        h = Mix(h, (ulong)(uint)year);
        foreach (var c in purpose)
        {
            h = Mix(h, c);
        }

        return new Random(unchecked((int)(h ^ (h >> 32))));
    }

    private static ulong Mix(ulong h, ulong value)
    {
        h ^= value;
        h *= FnvPrime;
        // A little extra avalanche so nearby years don't produce nearby seeds.
        h ^= h >> 27;
        h *= 0x9E3779B97F4A7C15UL;
        h ^= h >> 31;
        return h;
    }
}
