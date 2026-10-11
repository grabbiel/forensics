using System.Buffers.Binary;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;

namespace EvidenceChain.Api.Notifications;

/// <summary>
/// Where the next page of a reader's notifications starts: base64url of a version byte and the id the page reads below,
/// big-endian. Forward only. Not signed and carries no filter hash: every query filters by the token's user, so a forged
/// cursor only seeks within the reader's own rows. Not <c>InboxCursor</c>: that one is about evidence positions and
/// filters, and bending it to one id would cost both.
/// MVC binds it from the query through <see cref="TryParse(string?, IFormatProvider?, out NotificationCursor)"/>; a value
/// it refuses is a model-state error on <c>cursor</c>, which [ApiController] answers as a 400 validation problem, as the
/// inbox does.
/// </summary>
public sealed record NotificationCursor : IParsable<NotificationCursor>
{
    private const byte Version = 1;
    private const int Length = 1 + sizeof(long);
    private const int MaxChars = 64;

    private NotificationCursor(long beforeId) => BeforeId = beforeId;

    /// <summary>The next page holds the reader's notifications with ids below this one.</summary>
    public long BeforeId { get; }

    /// <summary>The cursor of the page after one whose last (oldest) item is <paramref name="lastId"/>.</summary>
    public static NotificationCursor After(long lastId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lastId, 1);
        return new NotificationCursor(lastId);
    }

    /// <summary>The wire form, for nextCursor.</summary>
    public override string ToString()
    {
        Span<byte> bytes = stackalloc byte[Length];
        bytes[0] = Version;
        BinaryPrimitives.WriteInt64BigEndian(bytes[1..], BeforeId);
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>False for anything this API did not issue: too long, wrong length, version or alphabet, or an id below 1.</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out NotificationCursor result)
    {
        result = null;
        if (string.IsNullOrEmpty(s) || s.Length > MaxChars)
            return false;
        Span<byte> bytes = stackalloc byte[Length];
        int written;
        try
        {
            // "Try" covers a short destination; characters outside base64url still throw.
            if (!Base64Url.TryDecodeFromChars(s, bytes, out written))
                return false;
        }
        catch (FormatException)
        {
            return false;
        }

        if (written != Length || bytes[0] != Version || BinaryPrimitives.ReadInt64BigEndian(bytes[1..]) is not (>= 1 and var id))
            return false;
        result = new NotificationCursor(id);
        return true;
    }

    public static NotificationCursor Parse(string s, IFormatProvider? provider) =>
        TryParse(s, provider, out var cursor) ? cursor : throw new FormatException("Not a notifications cursor.");
}
