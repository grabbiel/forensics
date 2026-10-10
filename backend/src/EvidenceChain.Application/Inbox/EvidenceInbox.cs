using EvidenceChain.Application.Common;
using EvidenceChain.Domain.Custody;

namespace EvidenceChain.Application.Inbox;

public enum InboxSort
{
    NewestFirst,
    OldestFirst,
}

/// <summary>A keyset position: one row's (last event time, evidence id), which a seek starts from and excludes.</summary>
public sealed record InboxPosition(DateTime LastEventAtUtc, long EvidenceId);

/// <summary>Which side of a position a seek reads, in display order. The values are the cursor's direction byte.</summary>
public enum SeekDirection : byte
{
    /// <summary>The rows displayed after the position.</summary>
    Forward = 0,

    /// <summary>The rows displayed before the position.</summary>
    Backward = 1,
}

/// <summary>Where a page starts: a position and the side of it to read.</summary>
public sealed record KeysetSeek
{
    private KeysetSeek(InboxPosition from, SeekDirection direction)
    {
        ArgumentNullException.ThrowIfNull(from);
        From = from;
        Direction = direction;
    }

    public InboxPosition From { get; }

    public SeekDirection Direction { get; }

    /// <summary>The page displayed after this row.</summary>
    public static KeysetSeek After(InboxPosition row) => new(row, SeekDirection.Forward);

    /// <summary>The page displayed before this row.</summary>
    public static KeysetSeek Before(InboxPosition row) => new(row, SeekDirection.Backward);

    /// <summary>For decoders. Refuses a direction byte the enum does not name.</summary>
    public static bool TryCreate(InboxPosition from, byte direction, out KeysetSeek? seek)
    {
        if (from is null || !Enum.IsDefined((SeekDirection)direction))
        {
            seek = null;
            return false;
        }

        seek = new KeysetSeek(from, (SeekDirection)direction);
        return true;
    }
}

/// <summary>Inbox filters; every one is optional.</summary>
/// <param name="Query">A code prefix (LOG2026…) or text anywhere in the description.</param>
/// <param name="Seek">Where the page starts; null for the first page.</param>
public sealed record EvidenceInboxFilter(
    string? Query,
    string? TypeCode,
    int? CustodianId,
    IntegrityStatus? Status,
    InboxSort Sort,
    int Limit,
    KeysetSeek? Seek = null);

/// <summary>The transfer waiting on its recipient, if any.</summary>
public sealed record PendingTransferSummary(long TransferId, int ToCustodianId, DateTime SinceUtc);

/// <summary>One inbox row.</summary>
public sealed record EvidenceSummary(
    string Code,
    string TypeCode,
    string Description,
    PersonRef CurrentCustodian,
    DateTime LastEventAtUtc,
    int EventCount,
    IntegrityStatus IntegrityStatus,
    DateTime? IntegrityCheckedAtUtc,
    PendingTransferSummary? PendingTransfer);

/// <summary>A page of rows in display order, with the seeks to its neighbours.</summary>
/// <param name="Next">After the last row; null on the last page.</param>
/// <param name="Previous">Before the first row; null on the first page.</param>
public sealed record InboxPage(IReadOnlyList<EvidenceSummary> Items, KeysetSeek? Next, KeysetSeek? Previous)
{
    public KeysetSeek? Next { get; } = Next is null or { Direction: SeekDirection.Forward }
        ? Next : throw new ArgumentException("Next reads forward.", nameof(Next));

    public KeysetSeek? Previous { get; } = Previous is null or { Direction: SeekDirection.Backward }
        ? Previous : throw new ArgumentException("Previous reads backward.", nameof(Previous));
}

/// <summary>Read side of the inbox, over the EvidenceInbox projection.</summary>
public interface IEvidenceInboxQuery
{
    /// <summary>
    /// One page in display order. A backward seek that reaches the start of the listing answers with the first page
    /// itself (Previous null), so previous from page 2 is exactly page 1 even if rows moved meanwhile.
    /// </summary>
    Task<InboxPage> ListAsync(EvidenceInboxFilter filter, CancellationToken cancellationToken);
}
