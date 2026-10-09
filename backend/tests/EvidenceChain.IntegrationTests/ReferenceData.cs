using System.Security.Cryptography;
using EvidenceChain.Domain.Integrity;
using EvidenceChain.SyntheticData;

namespace EvidenceChain.IntegrationTests;

/// <summary>The reference dataset (seed 42) and the key every test database signs it with.</summary>
public static class ReferenceData
{
    public const string KeyId = "test";

    private static readonly byte[] KeyBytes = SHA256.HashData("seed-tests"u8.ToArray());

    public static readonly string KeyBase64 = Convert.ToBase64String(KeyBytes);

    public static readonly IntegrityKeyRing Keys = new(KeyId, new Dictionary<string, byte[]> { [KeyId] = KeyBytes });

    public static readonly Lazy<SyntheticDataset> Dataset = new(() => DatasetBuilder.Build(ReferenceDataset.Seed, ReferenceDataset.AnchorUtc));
}

/// <summary>A clock stopped at one instant.</summary>
public sealed class FixedClock(DateTime nowUtc) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(nowUtc, TimeSpan.Zero);
}
