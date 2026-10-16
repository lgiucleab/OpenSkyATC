namespace OpenSkyAtc.Core.Random;

/// <summary>Seeded randomness for the brain, so a replay with the same seed gives the same session.</summary>
public interface IRandom
{
    /// <summary>Returns an integer in [minInclusive, maxExclusive).</summary>
    int Next(int minInclusive, int maxExclusive);

    /// <summary>Returns a double in [0, 1).</summary>
    double NextDouble();
}

/// <summary>
/// SplitMix64 generator. Implemented here instead of using System.Random so the sequence
/// never changes between .NET versions.
/// </summary>
public sealed class SeededRandom(int seed) : IRandom
{
    private ulong _state = unchecked((ulong)seed);

    private ulong NextUInt64()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public int Next(int minInclusive, int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(minInclusive, maxExclusive);
        ulong range = (ulong)((long)maxExclusive - minInclusive);
        return (int)(minInclusive + (long)(NextUInt64() % range));
    }
}
