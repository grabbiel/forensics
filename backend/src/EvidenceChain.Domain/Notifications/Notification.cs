using EvidenceChain.Domain.Custody;

namespace EvidenceChain.Domain.Notifications;

/// <summary>
/// What happened to a transfer, as a notification names it. Exactly the three transfer steps: registering evidence
/// notifies no one, so <see cref="CustodyEventKind.EvidenceRegistered"/> has no counterpart. The names match the
/// event kinds and are stored as strings under a CHECK built from this enum.
/// </summary>
public enum NotificationKind
{
    TransferRequested,
    TransferAccepted,
    TransferRejected,
}

/// <summary>
/// One person told about one transfer step. A convenience view like the inbox: it cites the transfer and the kind,
/// never the custody event (whose id is only known after its insert), and nothing in the chain reads it.
/// Rows are only made by <see cref="For"/>, so who is told, the kind and the time cannot drift from the transfer.
/// </summary>
public sealed class Notification
{
    private Notification(int recipientId, long transferId, NotificationKind kind, DateTime createdAtUtc)
    {
        RecipientId = recipientId;
        TransferId = transferId;
        Kind = kind;
        CreatedAtUtc = createdAtUtc;
    }

    // Required by EF Core for materialization.
    private Notification() { }

    public long NotificationId { get; private set; }

    public int RecipientId { get; private set; }

    public long TransferId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The step's own time: the transfer's request or decision time, the same clock reading as its event.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Null while unread. Only the read side sets it, by a column update; nothing here changes it.</summary>
    public DateTime? ReadAtUtc { get; private set; }

    /// <summary>
    /// The notifications of the step <paramref name="transfer"/> has just taken, one per person it concerns, in
    /// recipient order. Call it right after the transition: the step, its actor and its time are read from the
    /// transfer itself, so no argument can disagree with the event appended beside it.
    /// <list type="bullet">
    /// <item>Requested: the recipient (who decides), the holder (who is asked to hand it over), every Supervisor.</item>
    /// <item>Accepted or rejected: the requester, every Supervisor.</item>
    /// </list>
    /// The actor is removed and each person appears once. The domain already keeps the actor out (only an Investigador
    /// requests, only the recipient decides), but the rule does not rely on it.
    /// <paramref name="supervisorIds"/> are the Supervisors as the caller last loaded them: one added later is told
    /// only about steps taken after they appear there, and earlier ones are not backfilled.
    /// </summary>
    /// <exception cref="InvalidOperationException">The transfer has no id yet: save it before its notifications.</exception>
    public static IReadOnlyList<Notification> For(CustodyTransfer transfer, IReadOnlySet<int> supervisorIds)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentNullException.ThrowIfNull(supervisorIds);
        if (transfer.TransferId == 0)
            throw new InvalidOperationException("Save the transfer first: its notifications cite its id.");

        var (kind, actorId, at, parties) = transfer.Status switch
        {
            TransferStatus.Pending => (NotificationKind.TransferRequested, transfer.RequestedById, transfer.RequestedAtUtc,
                new[] { transfer.ToCustodianId, transfer.FromCustodianId }),
            TransferStatus.Accepted => (NotificationKind.TransferAccepted, transfer.DecidedById!.Value, transfer.DecidedAtUtc!.Value,
                new[] { transfer.RequestedById }),
            TransferStatus.Rejected => (NotificationKind.TransferRejected, transfer.DecidedById!.Value, transfer.DecidedAtUtc!.Value,
                new[] { transfer.RequestedById }),
            _ => throw new ArgumentOutOfRangeException(nameof(transfer), transfer.Status, "Unknown transfer status."),
        };
        return parties.Concat(supervisorIds).Where(id => id != actorId).Distinct().Order()
            .Select(id => new Notification(id, transfer.TransferId, kind, at)).ToList();
    }
}
