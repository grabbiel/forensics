using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.Custody;
using EvidenceChain.Domain.People;

namespace EvidenceChain.UnitTests.Custody;

public sealed class CustodyModelTests
{
    private static readonly DateTime Requested = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);
    private static byte[] Hash(byte fill) => Enumerable.Repeat(fill, 32).ToArray();

    [Fact]
    public void Content_hash_cannot_drift_from_its_bytes()
    {
        var content = new EvidenceContent([1, 2, 3], "text/plain");
        var exposed = content.Bytes;
        exposed[0] = 9;
        content.Sha256[0] ^= 0xFF;

        Assert.Equal([1, 2, 3], content.Bytes);
        Assert.Equal(System.Security.Cryptography.SHA256.HashData([1, 2, 3]), content.Sha256);
    }

    private static readonly Actor Investigator = new(1, UserRole.Investigador);
    private static readonly Actor Holder = new(4, UserRole.Custodio);
    private static readonly Actor Recipient = new(5, UserRole.Custodio);

    private static Evidence HeldBy4() =>
        new(EvidenceTypes.Log, DateOnly.FromDateTime(Requested), 1, "Log", Requested.AddHours(-1), Requested.AddMinutes(-30), 1, 4, new EvidenceContent([1], "text/plain"));

    private static CustodyTransfer RequestTo5(Evidence evidence) =>
        CustodyTransfer.Request(evidence, Investigator, Recipient, hasPendingTransfer: false, Requested, "Análisis", Guid.CreateVersion7(), Hash(1));

    [Fact]
    public void A_request_starts_from_the_current_custodian_and_needs_an_investigator_and_another_custodio()
    {
        var evidence = HeldBy4();
        var transfer = RequestTo5(evidence);

        Assert.Equal((4, 5, 1, TransferStatus.Pending), (transfer.FromCustodianId, transfer.ToCustodianId, transfer.RequestedById, transfer.Status));
        Assert.Throws<RoleNotAllowedException>(() => CustodyTransfer.Request(evidence, Holder, Recipient, false, Requested, "x", Guid.CreateVersion7(), Hash(1)));
        Assert.Throws<InvalidTransitionException>(() => CustodyTransfer.Request(evidence, Investigator, Recipient, hasPendingTransfer: true, Requested, "x", Guid.CreateVersion7(), Hash(1)));
        Assert.Throws<ArgumentException>(() => CustodyTransfer.Request(evidence, Investigator, Holder, false, Requested, "x", Guid.CreateVersion7(), Hash(1)));
        Assert.Throws<ArgumentException>(() => CustodyTransfer.Request(evidence, Investigator, new Actor(10, UserRole.Supervisor), false, Requested, "x", Guid.CreateVersion7(), Hash(1)));
        Assert.Throws<ArgumentException>(() => CustodyTransfer.Request(evidence, Investigator, Investigator with { Role = UserRole.Custodio }, false, Requested, "x", Guid.CreateVersion7(), Hash(1)));
    }

    [Fact]
    public void Only_the_recipient_decides_once_and_acceptance_hands_custody_over()
    {
        var evidence = HeldBy4();
        var transfer = RequestTo5(evidence);

        Assert.Throws<NotRecipientException>(() => transfer.Accept(evidence, Holder, Requested.AddHours(1), null, Guid.CreateVersion7(), Hash(2)));
        Assert.Throws<RoleNotAllowedException>(() => transfer.Accept(evidence, new Actor(10, UserRole.Supervisor), Requested.AddHours(1), null, Guid.CreateVersion7(), Hash(2)));
        Assert.Throws<ArgumentException>(() => transfer.Accept(evidence, Recipient, Requested.AddHours(-1), null, Guid.CreateVersion7(), Hash(2)));
        transfer.Accept(evidence, Recipient, Requested.AddHours(1), "Recibida", Guid.CreateVersion7(), Hash(2));

        Assert.Equal((TransferStatus.Accepted, 5), (transfer.Status, evidence.CurrentCustodianId));
        var second = Assert.Throws<InvalidTransitionException>(() => transfer.Reject(evidence, Recipient, Requested.AddHours(2), "Tarde", Guid.CreateVersion7(), Hash(3)));
        Assert.Equal((TransferStatus.Accepted, 5, Requested.AddHours(1)), (second.CurrentStatus!.Value, second.ActedById!.Value, second.ActedAtUtc!.Value)); // what the 409 reports
    }

    [Fact]
    public void A_rejection_keeps_custody_where_it_was()
    {
        var evidence = HeldBy4();
        var transfer = RequestTo5(evidence);
        transfer.Reject(evidence, Recipient, Requested.AddHours(1), "Sin orden judicial", Guid.CreateVersion7(), Hash(2));

        Assert.Equal((TransferStatus.Rejected, 4), (transfer.Status, evidence.CurrentCustodianId));
    }

    [Fact]
    public void Chain_heads_advance_one_contiguous_event_at_a_time()
    {
        var evidence = new Evidence(EvidenceTypes.Log, DateOnly.FromDateTime(Requested), 1, "Log", Requested.AddHours(-1), Requested, 1, 4, new EvidenceContent([1], "text/plain"));

        Assert.Throws<InvalidOperationException>(() => evidence.AdvanceHead(2, Hash(1)));
        evidence.AdvanceHead(1, Hash(1));
        evidence.HeadMac![0] = 0;

        Assert.Equal(1, evidence.EventCount);
        Assert.Equal(Hash(1), evidence.HeadMac); // the mutation above hit a copy
        Assert.Throws<ArgumentException>(() => new Evidence(EvidenceTypes.Log, new DateOnly(2026, 10, 7), 1, "Log", Requested, Requested, 1, 4, new EvidenceContent([1], "text/plain")));
        Assert.Throws<ArgumentException>(() => new Evidence(EvidenceTypes.Log, DateOnly.FromDateTime(Requested), 1, "Log", Requested, DateTime.SpecifyKind(Requested, DateTimeKind.Local), 1, 4, new EvidenceContent([1], "text/plain")));
    }

    [Fact]
    public void Events_copy_their_macs_and_genesis_commits_the_content()
    {
        var mac = Hash(7);
        var genesis = CustodyEvent.Genesis(1, Requested, 1, 4, Hash(3), 10, "text/plain", "Registro", "dev", mac, 2);
        mac[0] = 0;
        genesis.Mac[1] = 0;

        Assert.Equal((1, CustodyEventKind.EvidenceRegistered, 4), (genesis.Seq, genesis.Kind, genesis.ToCustodianId!.Value));
        Assert.Equal(Hash(7), genesis.Mac);
        Assert.Null(genesis.PrevMac);
        Assert.Throws<ArgumentOutOfRangeException>(() => CustodyEvent.ForTransfer(1, 2, CustodyEventKind.EvidenceRegistered, Requested, 1, 1, 4, 5, "", "dev", Hash(1), Hash(2), 2));
    }
}
