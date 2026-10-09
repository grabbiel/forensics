using System.Buffers.Binary;

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
    public int Next(int minInclusive, int maxExclusive) => (int)NextInt64(minInclusive, maxExclusive);

    /// <summary>Uniform 64-bit integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    public long NextInt64(long minInclusive, long maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(minInclusive, maxExclusive);
        var range = (ulong)(maxExclusive - minInclusive);
        // Modulo bias is negligible for the ranges used here; determinism is what matters.
        return minInclusive + (long)(NextUInt64() % range);
    }

    /// <summary>Uniform double in [0, 1) with 53 bits of precision.</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>True with the given probability.</summary>
    public bool NextBool(double probability) => NextDouble() < probability;

    /// <summary>Uniformly chosen item.</summary>
    public T Pick<T>(IReadOnlyList<T> items) => items[Next(0, items.Count)];

    /// <summary>Fisher-Yates shuffle in place.</summary>
    public void Shuffle<T>(IList<T> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = Next(0, i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>Independent generator seeded from this one, so one consumer's draws never shift another's.</summary>
    public DeterministicRandom Fork() => new(NextUInt64());

    /// <summary>RFC 9562 UUIDv7 for the given UTC instant, with deterministic random bits.</summary>
    public Guid NextUuidV7(DateTime utc)
    {
        Span<byte> bytes = stackalloc byte[16];
        var unixMs = (ulong)((utc.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond);
        BinaryPrimitives.WriteUInt64BigEndian(bytes, unixMs << 16);
        BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], NextUInt64());
        var extra = NextUInt64();
        bytes[6] = (byte)(0x70 | (extra & 0x0F)); // version 7
        bytes[7] = (byte)(extra >> 8);
        bytes[8] = (byte)(0x80 | (bytes[8] & 0x3F)); // RFC variant
        return new Guid(bytes, bigEndian: true);
    }
}
