using EvidenceChain.Application.Common;
using EvidenceChain.Application.Inbox;
using EvidenceChain.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvidenceChain.Infrastructure.Inbox;

/// <summary>
/// Keyset pages over the A4 projection: the seek predicate is written out (LastEventAtUtc, then EvidenceId) so SQL
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

        var newestFirst = filter.Sort == InboxSort.NewestFirst;
        if (filter.After is { } after)
        {
            var (at, id) = (after.LastEventAtUtc, after.EvidenceId);
            rows = newestFirst
                ? rows.Where(r => r.LastEventAtUtc < at || (r.LastEventAtUtc == at && r.EvidenceId < id))
                : rows.Where(r => r.LastEventAtUtc > at || (r.LastEventAtUtc == at && r.EvidenceId > id));
        }

        rows = newestFirst
            ? rows.OrderByDescending(r => r.LastEventAtUtc).ThenByDescending(r => r.EvidenceId)
            : rows.OrderBy(r => r.LastEventAtUtc).ThenBy(r => r.EvidenceId);

        // One extra row says whether another page follows.
        var page = await rows.Take(filter.Limit + 1)
            .Select(r => new
            {
                Position = new InboxPosition(r.LastEventAtUtc, r.EvidenceId),
                Summary = new EvidenceSummary(
                    r.Code, r.TypeCode, r.Description, new PersonRef(r.CurrentCustodianId, r.CurrentCustodianName),
                    r.LastEventAtUtc, r.EventCount, r.IntegrityStatus, r.IntegrityCheckedAtUtc,
                    r.PendingTransferId == null ? null : new PendingTransferSummary(r.PendingTransferId.Value, r.PendingToCustodianId!.Value, r.PendingSinceUtc!.Value)),
            })
            .ToListAsync(cancellationToken);

        var items = page.Take(filter.Limit).ToList();
        return new InboxPage(items.Select(r => r.Summary).ToList(), page.Count > filter.Limit ? items[^1].Position : null);
    }
}
