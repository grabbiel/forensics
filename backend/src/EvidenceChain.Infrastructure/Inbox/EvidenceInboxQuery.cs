using EvidenceChain.Application.Common;
using EvidenceChain.Application.Inbox;
using EvidenceChain.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvidenceChain.Infrastructure.Inbox;

/// <summary>
/// Keyset pages over the EvidenceInbox projection: the seek predicate is written out (LastEventAtUtc, then EvidenceId) so SQL
/// Server can seek an index ending in (LastEventAtUtc DESC, EvidenceId DESC) instead of counting skipped rows.
/// </summary>
internal sealed class EvidenceInboxQuery(AppDbContext db) : IEvidenceInboxQuery
{
    public async Task<InboxPage> ListAsync(EvidenceInboxFilter filter, CancellationToken cancellationToken)
    {
        var rows = db.EvidenceInbox.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.TypeCode))
            rows = rows.Where(r => r.TypeCode == filter.TypeCode);
        if (filter.CustodianId is { } custodianId)
            rows = rows.Where(r => r.CurrentCustodianId == custodianId);
        if (filter.Status is { } status)
            rows = rows.Where(r => r.IntegrityStatus == status);
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var text = filter.Query.Trim();
            var codePrefix = text.ToUpperInvariant(); // codes are upper-case under a binary collation
            rows = rows.Where(r => r.Code.StartsWith(codePrefix) || r.Description.Contains(text));
        }

        var backward = filter.Seek is { Direction: SeekDirection.Backward };
        var nearestFirstDescending = filter.Sort == InboxSort.NewestFirst != backward;
        if (filter.Seek is { } seek)
        {
            var (at, id) = (seek.From.LastEventAtUtc, seek.From.EvidenceId);
            rows = nearestFirstDescending
                ? rows.Where(r => r.LastEventAtUtc < at || (r.LastEventAtUtc == at && r.EvidenceId < id))
                : rows.Where(r => r.LastEventAtUtc > at || (r.LastEventAtUtc == at && r.EvidenceId > id));
        }

        rows = nearestFirstDescending
            ? rows.OrderByDescending(r => r.LastEventAtUtc).ThenByDescending(r => r.EvidenceId)
            : rows.OrderBy(r => r.LastEventAtUtc).ThenBy(r => r.EvidenceId);

        var fetched = await rows.Take(filter.Limit + 1)
            .Select(r => new
            {
                Position = new InboxPosition(r.LastEventAtUtc, r.EvidenceId),
                Summary = new EvidenceSummary(
                    r.Code, r.TypeCode, r.Description, new PersonRef(r.CurrentCustodianId, r.CurrentCustodianName),
                    r.LastEventAtUtc, r.EventCount, r.IntegrityStatus, r.IntegrityCheckedAtUtc,
                    r.PendingTransferId == null ? null : new PendingTransferSummary(r.PendingTransferId.Value, r.PendingToCustodianId!.Value, r.PendingSinceUtc!.Value)),
            })
            .ToListAsync(cancellationToken);

        var hasBeyondSeek = fetched.Count > filter.Limit;
        var items = fetched.Take(filter.Limit).ToList();
        if (NeedsTrueFirstPage(backward, hasBeyondSeek, items.Count, filter.Limit))
            return await ListAsync(filter with { Seek = null }, cancellationToken);
        if (items.Count == 0)
            return new InboxPage([], null, null);

        if (backward)
            items.Reverse();

        var summaries = items.Select(r => r.Summary).ToList();
        return backward
            ? new InboxPage(summaries, KeysetSeek.After(items[^1].Position), hasBeyondSeek ? KeysetSeek.Before(items[0].Position) : null)
            : new InboxPage(summaries, hasBeyondSeek ? KeysetSeek.After(items[^1].Position) : null,
                filter.Seek is not null ? KeysetSeek.Before(items[0].Position) : null);
    }

    /// <summary>
    /// A backward read with no row beyond the seek has reached the start. Fewer than Limit rows means the listing
    /// moved under the cursor, so the true first page must be loaded without a seek.
    /// </summary>
    private static bool NeedsTrueFirstPage(bool backward, bool hasBeyondSeek, int itemCount, int limit) =>
        backward && !hasBeyondSeek && itemCount < limit;
}
