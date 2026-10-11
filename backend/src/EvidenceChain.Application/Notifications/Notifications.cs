using EvidenceChain.Application.Common;
using EvidenceChain.Domain.Notifications;
using EvidenceChain.Domain.People;

namespace EvidenceChain.Application.Notifications;

/// <summary>
/// One notification as its recipient reads it: what happened, to which evidence, between whom, and why. Structured,
/// with no sentence: the client words it for the reader. Everything but the read state is joined in from the transfer
/// and its evidence at read time, so nothing here can go stale.
/// </summary>
/// <param name="From">The holder when the transfer was requested.</param>
/// <param name="To">The recipient, the only one who decides.</param>
/// <param name="Reason">The request's reason.</param>
/// <param name="DecisionNotes">A rejection's reason, or an acceptance's notes; null while undecided.</param>
public sealed record NotificationItem(
    long NotificationId,
    NotificationKind Kind,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc,
    string EvidenceCode,
    long TransferId,
    PersonRef RequestedBy,
    PersonRef From,
    PersonRef To,
    string Reason,
    string? DecisionNotes);

/// <summary>A page, newest first, and the reader's unread count as of the same snapshot.</summary>
/// <param name="NextBeforeId">The id the next page reads below; null on the last page.</param>
public sealed record NotificationPage(IReadOnlyList<NotificationItem> Items, long? NextBeforeId, int UnreadCount);

/// <summary>
/// A reader's own notifications. Every method takes the reader and touches only their rows: there is no way to name
/// another user's notifications through this port, so privacy is not a check a caller can forget.
/// </summary>
public interface INotifications
{
    /// <summary>Up to <paramref name="limit"/> items with ids below <paramref name="beforeId"/> (the newest when null).</summary>
    Task<NotificationPage> PageAsync(Actor reader, long? beforeId, int limit, CancellationToken cancellationToken);

    /// <summary>The reader's unread count, from the filtered index alone.</summary>
    Task<int> UnreadCountAsync(Actor reader, CancellationToken cancellationToken);

    /// <summary>
    /// Marks one read; already read is fine. False when the reader has no notification with this id, whether it does
    /// not exist or is someone else's: the caller answers both the same.
    /// </summary>
    Task<bool> MarkReadAsync(Actor reader, long notificationId, CancellationToken cancellationToken);

    /// <summary>Marks the reader's unread notifications with id at most <paramref name="upToId"/>; newer ones stay unread.</summary>
    Task MarkReadUpToAsync(Actor reader, long upToId, CancellationToken cancellationToken);
}
