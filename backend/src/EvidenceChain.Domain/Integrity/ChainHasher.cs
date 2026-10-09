using System.Security.Cryptography;

namespace EvidenceChain.Domain.Integrity;

/// <summary>Integrity keys by id; the active one signs new events, older ones still verify.</summary>
public sealed class IntegrityKeyRing
{
    /// <summary>HMAC-SHA-256 keys shorter than its 32-byte output weaken it.</summary>
    public const int MinKeyBytes = 32;

    private readonly Dictionary<string, byte[]> _keys;

    public IntegrityKeyRing(string activeKeyId, IReadOnlyDictionary<string, byte[]> keys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeKeyId);
        _keys = keys.ToDictionary(k => k.Key, k => k.Value.ToArray(), StringComparer.Ordinal);
        foreach (var (id, key) in _keys)
        {
            if (key.Length < MinKeyBytes)
                throw new ArgumentException($"Integrity key '{id}' has {key.Length} bytes; at least {MinKeyBytes} are required.", nameof(keys));
        }
        if (!_keys.ContainsKey(activeKeyId))
            throw new ArgumentException($"The active key '{activeKeyId}' is not in the ring.", nameof(activeKeyId));
        ActiveKeyId = activeKeyId;
    }

    public string ActiveKeyId { get; }

    /// <summary>Builds a ring from base64 values, as configured under Integrity:Keys.</summary>
    public static IntegrityKeyRing FromBase64(string activeKeyId, IEnumerable<KeyValuePair<string, string>> base64Keys) =>
        new(activeKeyId, base64Keys.ToDictionary(k => k.Key, k => Convert.FromBase64String(k.Value)));

    /// <summary>The key for <paramref name="keyId"/>, if this ring holds it.</summary>
    public bool TryGetKey(string keyId, out byte[] key)
    {
        var found = _keys.TryGetValue(keyId, out var value);
        key = found ? value!.ToArray() : [];
        return found;
    }
}

/// <summary>B10 hash chain: each event's MAC is HMAC-SHA-256 over its canonical bytes, which include the previous MAC.</summary>
public static class ChainHasher
{
    /// <summary>MAC of <paramref name="link"/> with the key named by its KeyId.</summary>
    public static byte[] Mac(IntegrityKeyRing keys, ChainLink link) =>
        keys.TryGetKey(link.KeyId, out var key)
            ? HMACSHA256.HashData(key, CanonicalEvent.Encode(link))
            : throw new KeyNotFoundException($"Unknown integrity key '{link.KeyId}'.");

    /// <summary>Constant-time comparison, so timing reveals nothing about a MAC.</summary>
    public static bool Matches(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) =>
        CryptographicOperations.FixedTimeEquals(expected, actual);
}
