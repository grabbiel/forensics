using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.Integrity;

namespace EvidenceChain.Domain.Custody;

/// <summary>Appends transfer events to an evidence's chain, signed with the active key.</summary>
public static class CustodyLedger
{
    /// <summary>
    /// The next event for <paramref name="transfer"/>, linked to the current head, which then moves to it. The parties
    /// come from the transfer and the notes are its reason or decision notes, as the verifier reconciles them.
    /// </summary>
    public static CustodyEvent Append(
        IntegrityKeyRing keys, Evidence evidence, CustodyTransfer transfer, CustodyEventKind kind, int actorId, DateTime occurredAtUtc)
    {
        if (transfer.EvidenceId != evidence.EvidenceId)
            throw new ArgumentException("The transfer belongs to another evidence.", nameof(transfer));
        var previous = evidence.HeadMac ?? throw new InvalidOperationException("The evidence has no registration event to link to.");

        var notes = kind switch
        {
            CustodyEventKind.TransferRequested => transfer.Reason,
            CustodyEventKind.TransferAccepted or CustodyEventKind.TransferRejected => transfer.DecisionNotes ?? string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only transfer steps are appended here."),
        };
        var seq = evidence.EventCount + 1;
        var link = new ChainLink(evidence.Code, seq, kind, occurredAtUtc, actorId, transfer.FromCustodianId, transfer.ToCustodianId,
            notes, null, null, null, keys.ActiveKeyId, previous);
        var mac = ChainHasher.Mac(keys, link);
        var appended = CustodyEvent.ForTransfer(evidence.EvidenceId, seq, kind, occurredAtUtc, actorId, transfer.TransferId,
            transfer.FromCustodianId, transfer.ToCustodianId, notes, keys.ActiveKeyId, previous, mac, CanonicalEvent.Version);
        evidence.AdvanceHead(seq, mac);
        return appended;
    }
}
