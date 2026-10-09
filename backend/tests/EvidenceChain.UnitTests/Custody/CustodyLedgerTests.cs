using System.Security.Cryptography;
using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.Custody;
using EvidenceChain.Domain.Integrity;
using EvidenceChain.Domain.People;

namespace EvidenceChain.UnitTests.Custody;

public sealed class CustodyLedgerTests
{
    private static readonly IntegrityKeyRing Keys = new("k1", new Dictionary<string, byte[]> { ["k1"] = SHA256.HashData("ledger-tests"u8.ToArray()) });
    private static readonly DateTime Registered = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Content = "contenido\n"u8.ToArray();

    [Fact]
    public void Requests_and_decisions_appended_through_the_domain_verify_end_to_end()
    {
        var (evidence, chain) = Registered4();
        var transfers = new List<CustodyTransfer>();

        var accepted = Request(evidence, chain, transfers, id: 1, to: 5, at: Registered.AddHours(1));
        accepted.Accept(evidence, new Actor(5, UserRole.Custodio), Registered.AddHours(2), null, Guid.CreateVersion7(), new byte[32]);
        chain.Add(CustodyLedger.Append(Keys, evidence, accepted, CustodyEventKind.TransferAccepted, 5, Registered.AddHours(2)));

        var rejected = Request(evidence, chain, transfers, id: 2, to: 6, at: Registered.AddHours(3));
        rejected.Reject(evidence, new Actor(6, UserRole.Custodio), Registered.AddHours(4), " Sin orden judicial ", Guid.CreateVersion7(), new byte[32]);
        chain.Add(CustodyLedger.Append(Keys, evidence, rejected, CustodyEventKind.TransferRejected, 6, Registered.AddHours(4)));

        Request(evidence, chain, transfers, id: 3, to: 7, at: Registered.AddHours(5));

        Assert.Equal((6, 5), (evidence.EventCount, evidence.CurrentCustodianId));
        Assert.Equal(chain[^1].Mac, evidence.HeadMac);
        Assert.Equal("Sin orden judicial", chain[4].Notes); // the stored decision notes, as the verifier reconciles them
        var verdict = ChainVerifier.Verify(Keys, evidence, chain, transfers, evidence.Content);
        Assert.True(verdict.IsValid, verdict.Detail);
    }

    [Fact]
    public void Only_transfer_steps_of_the_same_evidence_are_appended()
    {
        var (evidence, chain) = Registered4();
        var (other, _) = Registered4();
        typeof(Evidence).GetProperty(nameof(Evidence.EvidenceId))!.SetValue(other, 2L);
        var transfer = Request(evidence, chain, [], id: 1, to: 5, at: Registered.AddHours(1));

        Assert.Throws<ArgumentException>(() => CustodyLedger.Append(Keys, other, transfer, CustodyEventKind.TransferAccepted, 5, Registered));
        Assert.Throws<ArgumentOutOfRangeException>(() => CustodyLedger.Append(Keys, evidence, transfer, CustodyEventKind.EvidenceRegistered, 5, Registered));
    }

    /// <summary>A request by investigator 1, appended to the chain like the API does.</summary>
    private static CustodyTransfer Request(Evidence evidence, List<CustodyEvent> chain, List<CustodyTransfer> transfers, long id, int to, DateTime at)
    {
        var transfer = CustodyTransfer.Request(evidence, new Actor(1, UserRole.Investigador), new Actor(to, UserRole.Custodio), null, at,
            $"Motivo {id}", Guid.CreateVersion7(), new byte[32]);
        typeof(CustodyTransfer).GetProperty(nameof(CustodyTransfer.TransferId))!.SetValue(transfer, id); // the database's identity
        chain.Add(CustodyLedger.Append(Keys, evidence, transfer, CustodyEventKind.TransferRequested, 1, at));
        transfers.Add(transfer);
        return transfer;
    }

    /// <summary>Registered by user 1 to custodian 4, with its signed genesis event.</summary>
    private static (Evidence, List<CustodyEvent>) Registered4()
    {
        var evidence = new Evidence(EvidenceTypes.Log, DateOnly.FromDateTime(Registered), 1, "Log", Registered.AddHours(-1), Registered, 1, 4, new EvidenceContent(Content, "text/plain"));
        var link = new ChainLink(evidence.Code, 1, CustodyEventKind.EvidenceRegistered, Registered, 1, null, 4, "Registro",
            SHA256.HashData(Content), Content.Length, "text/plain", "k1", null);
        var mac = ChainHasher.Mac(Keys, link);
        evidence.AdvanceHead(1, mac);
        return (evidence, [CustodyEvent.Genesis(0, Registered, 1, 4, link.ContentSha256!, Content.Length, "text/plain", "Registro", "k1", mac, CanonicalEvent.Version)]);
    }
}
