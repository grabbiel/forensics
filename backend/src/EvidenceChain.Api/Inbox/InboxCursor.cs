using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EvidenceChain.Application.Inbox;

namespace EvidenceChain.Api.Inbox;

/// <summary>
/// Opaque keyset cursors: a version byte, the seek's direction, its position (last event time, evidence id) and 8 bytes
/// of a hash of the filter and sort they were issued for, base64url-encoded. A cursor only continues the listing it came
/// from. Version 1 (no direction byte) is still read, as forward, so links issued before backward paging keep working.
/// Not signed: a forged position, or a flipped direction, only seeks elsewhere in rows the caller may already list.
/// </summary>
public static class InboxCursor
{
    private const byte Version = 2;
    private const byte LegacyVersion = 1;
    private const int HashLength = 8;
    private const int Length = 1 + 1 + sizeof(long) + sizeof(long) + HashLength;
    private const int LegacyLength = Length - 1;

    /// <summary>Always version 2, whichever direction.</summary>
    public static string Encode(KeysetSeek seek, EvidenceInboxFilter filter)
    {
        Span<byte> bytes = stackalloc byte[Length];
        bytes[0] = Version;
        bytes[1] = (byte)seek.Direction;
        BinaryPrimitives.WriteInt64BigEndian(bytes[2..], seek.From.LastEventAtUtc.Ticks);
        BinaryPrimitives.WriteInt64BigEndian(bytes[10..], seek.From.EvidenceId);
        FilterHash(filter).CopyTo(bytes[18..]);
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>False for anything this API did not issue for this same filter and sort.</summary>
    public static bool TryDecode(string value, EvidenceInboxFilter filter, out KeysetSeek? seek)
    {
        seek = null;
        Span<byte> bytes = stackalloc byte[Length];
        int written;
        try
        {
            // "Try" covers a short destination; characters outside base64url still throw.
            if (!Base64Url.TryDecodeFromChars(value, bytes, out written))
                return false;
        }
        catch (FormatException)
        {
            return false;
        }

        byte direction;
        int bodyAt;
        switch (written, bytes[0])
        {
            case (LegacyLength, LegacyVersion):
                direction = (byte)SeekDirection.Forward;
                bodyAt = 1;
                break;
            case (Length, Version):
                direction = bytes[1];
                bodyAt = 2;
                break;
            default:
                return false;
        }

        var body = bytes[bodyAt..written];

        if (!body[16..].SequenceEqual(FilterHash(filter)))
            return false;

        var ticks = BinaryPrimitives.ReadInt64BigEndian(body);
        var evidenceId = BinaryPrimitives.ReadInt64BigEndian(body[8..]);
        if (ticks is < 0 or > 3155378975999999999 || evidenceId < 1) // DateTime.MaxValue.Ticks
            return false;

        return KeysetSeek.TryCreate(new InboxPosition(new DateTime(ticks, DateTimeKind.Utc), evidenceId), direction, out seek);
    }

    /// <summary>Every filter and the sort; not the page size, which may change between pages, nor the seek.</summary>
    private static byte[] FilterHash(EvidenceInboxFilter filter)
    {
        var canonical = string.Join('\n',
            filter.Query?.Trim() ?? "", filter.TypeCode ?? "", filter.CustodianId?.ToString(CultureInfo.InvariantCulture) ?? "",
            filter.Status?.ToString() ?? "", filter.Sort.ToString());
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical))[..HashLength];
    }
}
