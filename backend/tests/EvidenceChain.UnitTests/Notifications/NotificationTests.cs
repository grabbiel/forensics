using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.Custody;
using EvidenceChain.Domain.Notifications;
using EvidenceChain.Domain.People;

namespace EvidenceChain.UnitTests.Notifications;

public sealed class NotificationTests
{
    private const int Investigator = 1, Holder = 4, Recipient = 5;
    private static readonly HashSet<int> Supervisors = [10, 11];
    private static readonly DateTime Registered = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Requested = Registered.AddHours(1);
    private static readonly DateTime Decided = Registered.AddHours(2);

    [Fact]
    public void A_request_tells_the_recipient_the_holder_and_every_supervisor()
    {
        var (_, transfer) = Requested7();

        Assert.Equal(
            [(Holder, NotificationKind.TransferRequested, Requested), (Recipient, NotificationKind.TransferRequested, Requested),
             (10, NotificationKind.TransferRequested, Requested), (11, NotificationKind.TransferRequested, Requested)],
            Shape(Notification.For(transfer, Supervisors)));
    }

    [Fact]
    public void A_request_does_not_tell_its_requester()
    {
        var (_, transfer) = Requested7();

        Assert.Equal([Holder, Recipient, 10], Notification.For(transfer, new HashSet<int> { Investigator, 10 }).Select(n => n.RecipientId));
    }

    [Fact]
    public void An_accept_tells_the_requester_and_every_supervisor()
    {
        var (evidence, transfer) = Requested7();
        transfer.Accept(evidence, new Actor(Recipient, UserRole.Custodio), Decided, null, Guid.CreateVersion7(), new byte[32]);

        Assert.Equal(
            [(Investigator, NotificationKind.TransferAccepted, Decided), (10, NotificationKind.TransferAccepted, Decided),
             (11, NotificationKind.TransferAccepted, Decided)],
            Shape(Notification.For(transfer, Supervisors)));
    }

    [Fact]
    public void A_reject_tells_the_requester_and_every_supervisor()
    {
        var (evidence, transfer) = Requested7();
        transfer.Reject(evidence, new Actor(Recipient, UserRole.Custodio), Decided, "Sin orden judicial", Guid.CreateVersion7(), new byte[32]);

        Assert.Equal(
            [(Investigator, NotificationKind.TransferRejected, Decided), (10, NotificationKind.TransferRejected, Decided),
             (11, NotificationKind.TransferRejected, Decided)],
            Shape(Notification.For(transfer, Supervisors)));
    }

    [Fact]
    public void A_decision_does_not_tell_the_decider()
    {
        var (evidence, transfer) = Requested7();
        transfer.Reject(evidence, new Actor(Recipient, UserRole.Custodio), Decided, "Sin orden judicial", Guid.CreateVersion7(), new byte[32]);

        Assert.Equal([Investigator, 10], Notification.For(transfer, new HashSet<int> { Recipient, 10 }).Select(n => n.RecipientId));
    }

    [Fact]
    public void No_one_is_told_twice()
    {
        var (_, transfer) = Requested7();

        Assert.Equal([Holder, Recipient, 10], Notification.For(transfer, new HashSet<int> { Holder, Recipient, 10 }).Select(n => n.RecipientId));
    }

    [Fact]
    public void Every_notification_cites_the_transfer_and_starts_unread()
    {
        var (_, transfer) = Requested7();

        Assert.All(Notification.For(transfer, Supervisors), n => Assert.Equal((7L, (DateTime?)null, 0L), (n.TransferId, n.ReadAtUtc, n.NotificationId)));
    }

    [Fact]
    public void An_unsaved_transfer_is_refused()
    {
        var transfer = CustodyTransfer.Request(Evidence4(), new Actor(Investigator, UserRole.Investigador), new Actor(Recipient, UserRole.Custodio),
            null, Requested, "Análisis", Guid.CreateVersion7(), new byte[32]);

        Assert.Throws<InvalidOperationException>(() => Notification.For(transfer, Supervisors));
    }

    private static IEnumerable<(int, NotificationKind, DateTime)> Shape(IEnumerable<Notification> notifications) =>
        notifications.Select(n => (n.RecipientId, n.Kind, n.CreatedAtUtc));

    /// <summary>Transfer 7: investigator 1 asks holder 4 to hand the evidence to custodian 5.</summary>
    private static (Evidence, CustodyTransfer) Requested7()
    {
        var evidence = Evidence4();
        var transfer = CustodyTransfer.Request(evidence, new Actor(Investigator, UserRole.Investigador), new Actor(Recipient, UserRole.Custodio),
            null, Requested, "Análisis", Guid.CreateVersion7(), new byte[32]);
        typeof(CustodyTransfer).GetProperty(nameof(CustodyTransfer.TransferId))!.SetValue(transfer, 7L); // the database's identity
        return (evidence, transfer);
    }

    private static Evidence Evidence4() =>
        new(EvidenceTypes.Log, DateOnly.FromDateTime(Registered), 1, "Log", Registered.AddHours(-1), Registered, Investigator, Holder,
            new EvidenceContent("contenido\n"u8.ToArray(), "text/plain"));
}
