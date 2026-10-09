using EvidenceChain.Application.Common;
using EvidenceChain.Domain.Custody;

namespace EvidenceChain.Application.Inbox;

public enum InboxSort
{
    NewestFirst,
    OldestFirst,
}

/// <summary>A keyset position: the last row of the previous page.</summary>
public sealed record InboxPosition(DateTime LastEventAtUtc, long EvidenceId);

/// <summary>Inbox filters; every one is optional.</summary>
/// <param name="Query">A code prefix (LOG2026…) or text anywhere in the description.</param>
public sealed record EvidenceInboxFilter(
    string? Query,
    string? TypeCode,
    int? CustodianId,
    IntegrityStatus? Status,
    InboxSort Sort,
    int Limit,
    InboxPosition? After = null);

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

/// <summary>A page of rows and, when more follow, where the next page starts.</summary>
public sealed record InboxPage(IReadOnlyList<EvidenceSummary> Items, InboxPosition? Next);

/// <summary>Read side of the inbox, over the A4 projection.</summary>
public interface IEvidenceInboxQuery
{
    Task<InboxPage> ListAsync(EvidenceInboxFilter filter, CancellationToken cancellationToken);
}
