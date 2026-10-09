using EvidenceChain.Domain.Custody;

namespace EvidenceChain.Application.Custody;

/// <summary>A transfer as it stands now; 409 responses carry it so the client can explain what happened.</summary>
public sealed record TransferState(
    long TransferId,
    long EvidenceId,
    TransferStatus Status,
    int FromCustodianId,
    int ToCustodianId,
    int RequestedById,
    DateTime RequestedAtUtc,
    int? DecidedById,
    DateTime? DecidedAtUtc)
{
    public static TransferState From(CustodyTransfer transfer) =>
        new(transfer.TransferId, transfer.EvidenceId, transfer.Status, transfer.FromCustodianId, transfer.ToCustodianId,
            transfer.RequestedById, transfer.RequestedAtUtc, transfer.DecidedById, transfer.DecidedAtUtc);
}

/// <summary>If-Match named a version the transfer has moved past; carries the current one.</summary>
public sealed class StaleVersionException(CustodyTransfer current)
    : Exception($"Transfer {current.TransferId} changed after that version was read; it is now {current.Status}.")
{
    public CustodyTransfer Current { get; } = current;
}
