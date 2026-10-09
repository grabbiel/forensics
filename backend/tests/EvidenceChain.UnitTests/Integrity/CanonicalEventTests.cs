using System.Buffers.Binary;
using System.Text;
using EvidenceChain.Domain.Custody;
using EvidenceChain.Domain.Integrity;

namespace EvidenceChain.UnitTests.Integrity;

public sealed class CanonicalEventTests
{
    private static readonly IntegrityKeyRing Keys = new("test", new Dictionary<string, byte[]> { ["test"] = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray() });

    private static readonly ChainLink Genesis = new("LOG202610080001", 1, CustodyEventKind.EvidenceRegistered, new DateTime(2026, 10, 8, 9, 30, 0, DateTimeKind.Utc),
        ActorId: 1, FromCustodianId: null, ToCustodianId: 4, "Registro inicial", Enumerable.Repeat((byte)0xAB, 32).ToArray(), 1284, "text/plain; charset=utf-8", "test", PrevMac: null);

    [Fact]
    public void Encoding_v2_is_a_version_byte_then_length_prefixed_fields()
    {
        var bytes = CanonicalEvent.Encode(Genesis);

        Assert.Equal(2, bytes[0]);
        Assert.Equal(187, bytes.Length);
        Assert.Equal(15u, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1)));
        Assert.Equal("LOG202610080001", Encoding.ASCII.GetString(bytes, 5, 15));
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(24))); // seq after its 4-byte length
        Assert.Equal(0xFFFFFFFFu, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(bytes.Length - 4))); // null PrevMac
    }

    [Fact]
    public void The_mac_of_a_known_link_never_changes()
    {
        // Pins the format: any change to the encoding breaks every stored chain, so it must be deliberate.
        Assert.Equal("51713344e714ffdab8e93dd6d140fd5ef9ecaf47da6dffe6d09b17bd378a9f04", Convert.ToHexStringLower(ChainHasher.Mac(Keys, Genesis)));
    }

    [Fact]
    public void Every_field_changes_the_mac()
    {
        var original = ChainHasher.Mac(Keys, Genesis);
        ChainLink[] altered =
        [
            Genesis with { EvidenceCode = "LOG202610080002" }, Genesis with { Seq = 2 }, Genesis with { Kind = CustodyEventKind.TransferRequested },
            Genesis with { OccurredAtUtc = Genesis.OccurredAtUtc.AddTicks(1) }, Genesis with { ActorId = 2 }, Genesis with { FromCustodianId = 4 },
            Genesis with { ToCustodianId = 5 }, Genesis with { Notes = "Registro inicial." }, Genesis with { ContentSha256 = new byte[32] },
            Genesis with { ContentLength = 1285 }, Genesis with { MediaType = "text/csv; charset=utf-8" }, Genesis with { PrevMac = new byte[32] },
        ];

        Assert.All(altered, link => Assert.False(ChainHasher.Matches(original, ChainHasher.Mac(Keys, link)), link.ToString()));
    }

    [Fact]
    public void Text_is_normalized_and_null_differs_from_empty()
    {
        var composed = Genesis with { Notes = "Recibida por Óscar" };
        var decomposed = Genesis with { Notes = "Recibida por Óscar" };

        Assert.Equal(CanonicalEvent.Encode(composed), CanonicalEvent.Encode(decomposed));
        Assert.NotEqual(CanonicalEvent.Encode(Genesis with { MediaType = null }), CanonicalEvent.Encode(Genesis with { MediaType = "" }));
    }

    [Fact]
    public void The_key_ring_rejects_weak_unknown_and_missing_keys()
    {
        Assert.Throws<ArgumentException>(() => new IntegrityKeyRing("short", new Dictionary<string, byte[]> { ["short"] = new byte[16] }));
        Assert.Throws<ArgumentException>(() => new IntegrityKeyRing("k2", new Dictionary<string, byte[]> { ["k1"] = new byte[32] }));
        Assert.Throws<KeyNotFoundException>(() => ChainHasher.Mac(Keys, Genesis with { KeyId = "k9" }));
        Assert.Throws<ArgumentException>(() => CanonicalEvent.Encode(Genesis with { OccurredAtUtc = DateTime.SpecifyKind(Genesis.OccurredAtUtc, DateTimeKind.Unspecified) }));
        Assert.Throws<NotSupportedException>(() => ChainHasher.Mac(Keys, Genesis with { Version = 3 }));
    }
}
