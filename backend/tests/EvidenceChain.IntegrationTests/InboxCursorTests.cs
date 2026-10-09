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
        var cursor = InboxCursor.Encode(Position, Filter);

        Assert.Matches("^[A-Za-z0-9_-]+$", cursor); // base64url, safe in a query string
        Assert.True(InboxCursor.TryDecode(cursor, Filter with { Limit = 50 }, out var decoded));
        Assert.Equal(Position, decoded);
        Assert.Equal(DateTimeKind.Utc, decoded!.LastEventAtUtc.Kind);
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
        Assert.False(InboxCursor.TryDecode(InboxCursor.Encode(Position, Filter), other, out _));
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
        var cursor = InboxCursor.Encode(Position, Filter).ToCharArray();
        cursor[30] = cursor[30] == 'A' ? 'B' : 'A';
        Assert.False(InboxCursor.TryDecode(new string(cursor), Filter, out _));
    }
}
