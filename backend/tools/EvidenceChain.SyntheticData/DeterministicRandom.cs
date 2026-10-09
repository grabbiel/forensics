namespace EvidenceChain.SyntheticData;

/// <summary>SplitMix64: same seed, same bytes on every .NET version (seeded System.Random gives no such promise).</summary>
public sealed class DeterministicRandom(ulong seed)
{
    private ulong _state = seed;

    /// <summary>Next raw 64-bit value.</summary>
    public ulong NextUInt64()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    public int Next(int minInclusive, int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(minInclusive, maxExclusive);
        var range = (ulong)((long)maxExclusive - minInclusive);
        // Modulo bias is below 1e-9 for ranges this small; determinism is what matters here.
        return (int)(minInclusive + (long)(NextUInt64() % range));
    }

    /// <summary>Uniform double in [0, 1) with 53 bits of precision.</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));
}
