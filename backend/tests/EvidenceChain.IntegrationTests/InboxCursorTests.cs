using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EvidenceChain.Api.Inbox;
using EvidenceChain.Application.Inbox;
using EvidenceChain.Domain.Custody;

namespace EvidenceChain.IntegrationTests;

public sealed class InboxCursorTests
{
    private static readonly EvidenceInboxFilter Filter = new("LOG2026", "LOG", 4, IntegrityStatus.Valid, InboxSort.NewestFirst, 25);
    private static readonly InboxPosition Position = new(new DateTime(2026, 9, 30, 23, 59, 59, 123, DateTimeKind.Utc).AddTicks(4567), 991);

    [Fact]
    public void A_cursor_round_trips_for_its_own_filter_whatever_the_page_size()
    {
        var cursor = InboxCursor.Encode(KeysetSeek.After(Position), Filter);

        Assert.Matches("^[A-Za-z0-9_-]+$", cursor); // base64url, safe in a query string
        Assert.True(cursor.Length <= 64);
        Assert.True(InboxCursor.TryDecode(cursor, Filter with { Limit = 50 }, out var decoded));
        Assert.Equal(KeysetSeek.After(Position), decoded);
        Assert.Equal(DateTimeKind.Utc, decoded!.From.LastEventAtUtc.Kind);
    }

    [Fact]
    public void A_backward_cursor_round_trips_with_its_direction_and_refuses_another_sort()
    {
        var seek = KeysetSeek.Before(Position);
        var cursor = InboxCursor.Encode(seek, Filter);

        Assert.True(InboxCursor.TryDecode(cursor, Filter with { Limit = 10 }, out var decoded));
        Assert.Equal(seek, decoded);
        Assert.False(InboxCursor.TryDecode(cursor, Filter with { Sort = InboxSort.OldestFirst }, out _));
    }

    [Fact]
    public void A_version_1_cursor_still_decodes_as_forward()
    {
        Assert.True(InboxCursor.TryDecode(HandEncodedV1(Position, Filter), Filter, out var seek));
        Assert.Equal(KeysetSeek.After(Position), seek);
    }

    [Fact]
    public void A_cursor_with_an_unknown_direction_is_refused()
    {
        var bytes = Base64Url.DecodeFromChars(InboxCursor.Encode(KeysetSeek.After(Position), Filter));
        bytes[1] = 2;

        Assert.False(InboxCursor.TryDecode(Base64Url.EncodeToString(bytes), Filter, out _));
    }

    public static TheoryData<EvidenceInboxFilter> OtherFilters() =>
    [
        Filter with { Query = "LOG2025" },
        Filter with { TypeCode = "CSV" },
        Filter with { CustodianId = 5 },
        Filter with { Status = IntegrityStatus.Invalid },
        Filter with { Sort = InboxSort.OldestFirst },
    ];

    [Theory, MemberData(nameof(OtherFilters))]
    public void A_cursor_never_continues_another_filter_or_sort(EvidenceInboxFilter other)
    {
        Assert.False(InboxCursor.TryDecode(InboxCursor.Encode(KeysetSeek.After(Position), Filter), other, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a cursor")]
    [InlineData("AQ")]
    public void Anything_else_is_refused(string value)
    {
        Assert.False(InboxCursor.TryDecode(value, Filter, out _));
    }

    [Fact]
    public void An_altered_cursor_is_refused()
    {
        // A character inside the filter hash; the last one also carries padding bits a decoder may ignore.
        var cursor = InboxCursor.Encode(KeysetSeek.After(Position), Filter).ToCharArray();
        cursor[30] = cursor[30] == 'A' ? 'B' : 'A';
        Assert.False(InboxCursor.TryDecode(new string(cursor), Filter, out _));
    }

    /// <summary>The 25-byte layout issued before direction existed: version 1, ticks, id, then the filter hash.</summary>
    private static string HandEncodedV1(InboxPosition position, EvidenceInboxFilter filter)
    {
        Span<byte> bytes = stackalloc byte[25];
        bytes[0] = 1;
        BinaryPrimitives.WriteInt64BigEndian(bytes[1..], position.LastEventAtUtc.Ticks);
        BinaryPrimitives.WriteInt64BigEndian(bytes[9..], position.EvidenceId);
        var canonical = string.Join('\n',
            filter.Query?.Trim() ?? "", filter.TypeCode ?? "", filter.CustodianId?.ToString(CultureInfo.InvariantCulture) ?? "",
            filter.Status?.ToString() ?? "", filter.Sort.ToString());
        SHA256.HashData(Encoding.UTF8.GetBytes(canonical)).AsSpan(0, 8).CopyTo(bytes[17..]);
        return Base64Url.EncodeToString(bytes);
    }
}
