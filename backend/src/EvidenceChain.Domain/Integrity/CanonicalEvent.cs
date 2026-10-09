using System.Buffers.Binary;
using System.Text;
using EvidenceChain.Domain.Custody;

namespace EvidenceChain.Domain.Integrity;

/// <summary>
/// The fields a custody event's MAC covers. Database ids are left out on purpose: the evidence code,
/// fixed user ids and the previous MAC identify a link, so a chain verifies the same on any copy of the data.
/// </summary>
public sealed record ChainLink(
    string EvidenceCode,
    int Seq,
    CustodyEventKind Kind,
    DateTime OccurredAtUtc,
    int ActorId,
    int? FromCustodianId,
    int? ToCustodianId,
    string Notes,
    byte[]? ContentSha256,
    int? ContentLength,
    string? MediaType,
    string KeyId,
    byte[]? PrevMac,
    byte Version = CanonicalEvent.Version)
{
    /// <summary>The link as stored, including its recorded encoding version, for verification.</summary>
    public static ChainLink From(string evidenceCode, CustodyEvent e) =>
        new(evidenceCode, e.Seq, e.Kind, e.OccurredAtUtc, e.ActorId, e.FromCustodianId, e.ToCustodianId, e.Notes,
            e.ContentSha256, e.ContentLength, e.MediaType, e.KeyId, e.PrevMac, e.CanonicalVersion);
}

/// <summary>
/// Canonical encoding v2: a version byte, then each field in a fixed order as a big-endian uint32 length and its bytes.
/// Null is length 0xFFFFFFFF with no bytes; text is NFC UTF-8; instants are UTC ticks as big-endian int64.
/// </summary>
public static class CanonicalEvent
{
    public const byte Version = 2;
    private const uint Null = 0xFFFFFFFF;

    /// <summary>Encodes the link; equal links always give equal bytes.</summary>
    public static byte[] Encode(ChainLink link)
    {
        // An edited version number must fail verification, not be silently re-encoded as v2.
        if (link.Version != Version)
            throw new NotSupportedException($"Canonical encoding v{link.Version} is not supported; this build encodes v{Version}.");
        Utc.Require(link.OccurredAtUtc, nameof(link));
        using var stream = new MemoryStream(256);
        stream.WriteByte(Version);
        Text(stream, link.EvidenceCode);
        Int32(stream, link.Seq);
        Text(stream, link.Kind.ToString());
        Int64(stream, link.OccurredAtUtc.Ticks);
        Int32(stream, link.ActorId);
        Int32(stream, link.FromCustodianId);
        Int32(stream, link.ToCustodianId);
        Text(stream, link.Notes);
        Bytes(stream, link.ContentSha256);
        Int32(stream, link.ContentLength);
        Text(stream, link.MediaType);
        Text(stream, link.KeyId);
        Bytes(stream, link.PrevMac);
        return stream.ToArray();
    }

    private static void Text(Stream stream, string? value) =>
        Bytes(stream, value is null ? null : Encoding.UTF8.GetBytes(value.Normalize(NormalizationForm.FormC)));

    private static void Int32(Stream stream, int? value)
    {
        if (value is not { } v) { Bytes(stream, null); return; }
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, v);
        Bytes(stream, buffer.ToArray());
    }

    private static void Int64(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        Bytes(stream, buffer.ToArray());
    }

    private static void Bytes(Stream stream, byte[]? value)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, value is null ? Null : (uint)value.Length);
        stream.Write(length);
        if (value is not null)
            stream.Write(value);
    }
}
