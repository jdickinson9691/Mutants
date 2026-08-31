namespace ChronTravelers.Engine.Tests;

/// <summary>
/// Deterministic <see cref="IRandomSource"/> for tests: either a fixed
/// value every call, or a scripted sequence (repeating the last value once
/// the sequence is exhausted, so tests don't need to over-specify length).
/// </summary>
public sealed class StubRandomSource : IRandomSource
{
    private readonly double[] _sequence;
    private int _index;

    public StubRandomSource(params double[] sequence)
    {
        if (sequence.Length == 0)
        {
            throw new ArgumentException("Must supply at least one value.", nameof(sequence));
        }

        _sequence = sequence;
    }

    public static StubRandomSource Fixed(double value) => new(value);

    public double NextDouble()
    {
        var value = _sequence[Math.Min(_index, _sequence.Length - 1)];
        _index++;
        return value;
    }
}
