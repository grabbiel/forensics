using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EvidenceChain.Application.Inbox;

namespace EvidenceChain.Api.Inbox;

/// <summary>
/// Opaque keyset cursors: a version byte, the position (last event time, evidence id) and 8 bytes of a hash of the
/// filter and sort they were issued for, base64url-encoded. A cursor only continues the listing it came from.
/// Not signed: a forged position only seeks elsewhere in rows the caller may already list.
/// </summary>
public static class InboxCursor
{
    private const byte Version = 1;
    private const int HashLength = 8;
    private const int Length = 1 + sizeof(long) + sizeof(long) + HashLength;

    public static string Encode(InboxPosition position, EvidenceInboxFilter filter)
    {
        Span<byte> bytes = stackalloc byte[Length];
        bytes[0] = Version;
        BinaryPrimitives.WriteInt64BigEndian(bytes[1..], position.LastEventAtUtc.Ticks);
        BinaryPrimitives.WriteInt64BigEndian(bytes[9..], position.EvidenceId);
        FilterHash(filter).CopyTo(bytes[17..]);
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>False for anything this API did not issue for this same filter and sort.</summary>
    public static bool TryDecode(string value, EvidenceInboxFilter filter, out InboxPosition? position)
    {
        position = null;
        Span<byte> bytes = stackalloc byte[Length];
        try
        {
            // "Try" covers a short destination; characters outside base64url still throw.
            if (!Base64Url.TryDecodeFromChars(value, bytes, out var written) || written != Length || bytes[0] != Version)
                return false;
        }
        catch (FormatException)
        {
            return false;
        }
        if (!bytes[17..].SequenceEqual(FilterHash(filter)))
            return false;

        var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes[1..]);
        var evidenceId = BinaryPrimitives.ReadInt64BigEndian(bytes[9..]);
        if (ticks is < 0 or > 3155378975999999999 || evidenceId < 1) // DateTime.MaxValue.Ticks
            return false;

        position = new InboxPosition(new DateTime(ticks, DateTimeKind.Utc), evidenceId);
        return true;
    }

    /// <summary>Every filter and the sort; not the page size, which may change between pages.</summary>
    private static byte[] FilterHash(EvidenceInboxFilter filter)
    {
        var canonical = string.Join('\n',
            filter.Query?.Trim() ?? "", filter.TypeCode ?? "", filter.CustodianId?.ToString(CultureInfo.InvariantCulture) ?? "",
            filter.Status?.ToString() ?? "", filter.Sort.ToString());
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical))[..HashLength];
    }
}
