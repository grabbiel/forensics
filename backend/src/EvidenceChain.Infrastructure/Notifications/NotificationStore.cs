using EvidenceChain.Application.Notifications;
using EvidenceChain.Domain.Notifications;
using EvidenceChain.Domain.People;
using EvidenceChain.Infrastructure.People;
using EvidenceChain.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvidenceChain.Infrastructure.Notifications;

/// <summary>
/// The read side of notifications and the only writer of their read state. Every query starts from <see cref="Mine"/>,
/// so the recipient filter is written once. Marking read is a column update, the app login's only update grant on the
/// table; it never loads or deletes rows.
/// </summary>
internal sealed class NotificationStore(AppDbContext db, TimeProvider clock, PeopleIndexProvider peopleIndex) : INotifications
{
    public async Task<NotificationPage> PageAsync(Actor reader, long? beforeId, int limit, CancellationToken cancellationToken)
    {
        // One snapshot: the count describes the same moment as the page.
        var (rows, unread) = await db.InSnapshotAsync(async () =>
        {
            var rows = await Mine(reader)
                .Where(n => beforeId == null || n.NotificationId < beforeId)
                .OrderByDescending(n => n.NotificationId)
                .Take(limit + 1)
                .Join(db.CustodyTransfers, n => n.TransferId, t => t.TransferId, (n, t) => new { n, t })
                .Join(db.Evidence, x => x.t.EvidenceId, e => e.EvidenceId, (x, e) => new
                {
                    x.n.NotificationId, x.n.Kind, x.n.CreatedAtUtc, x.n.ReadAtUtc, e.Code, x.t.TransferId,
                    x.t.RequestedById, x.t.FromCustodianId, x.t.ToCustodianId, x.t.Reason, x.t.DecisionNotes,
                })
                .OrderByDescending(r => r.NotificationId)
                .ToListAsync(cancellationToken);
            return (rows, await Unread(reader).CountAsync(cancellationToken));
        }, cancellationToken);

        var people = await peopleIndex.GetAsync(cancellationToken);
        var items = rows.Take(limit).Select(r => new NotificationItem(
            r.NotificationId, r.Kind, r.CreatedAtUtc, r.ReadAtUtc, r.Code, r.TransferId,
            people.Of(r.RequestedById), people.Of(r.FromCustodianId), people.Of(r.ToCustodianId), r.Reason, r.DecisionNotes)).ToList();
        return new NotificationPage(items, rows.Count > limit ? items[^1].NotificationId : null, unread);
    }

    public Task<int> UnreadCountAsync(Actor reader, CancellationToken cancellationToken) => Unread(reader).CountAsync(cancellationToken);

    public async Task<bool> MarkReadAsync(Actor reader, long notificationId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (await Unread(reader).Where(n => n.NotificationId == notificationId)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, now), cancellationToken) == 1)
            return true;
        // Nothing changed: already read, or not the reader's. Same scope, so an id leaks nothing.
        return await Mine(reader).AnyAsync(n => n.NotificationId == notificationId, cancellationToken);
    }

    public Task MarkReadUpToAsync(Actor reader, long upToId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return Unread(reader).Where(n => n.NotificationId <= upToId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, now), cancellationToken);
    }

    /// <summary>The reader's rows, untracked. The only way into the table from here.</summary>
    private IQueryable<Notification> Mine(Actor reader) =>
        db.Notifications.AsNoTracking().Where(n => n.RecipientId == reader.UserId);

    /// <summary>What the filtered index holds for the reader.</summary>
    private IQueryable<Notification> Unread(Actor reader) => Mine(reader).Where(n => n.ReadAtUtc == null);
}
