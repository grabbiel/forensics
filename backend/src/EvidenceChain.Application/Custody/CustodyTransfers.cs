using System.Text.Json.Serialization;
using EvidenceChain.Application.Common;
using EvidenceChain.Domain.Custody;
using EvidenceChain.Domain.People;

namespace EvidenceChain.Application.Custody;

/// <summary>A transfer as clients see it, with the ETag that accept and reject send back as If-Match.</summary>
public sealed record TransferResource(
    long TransferId,
    string EvidenceCode,
    TransferStatus Status,
    PersonRef From,
    PersonRef To,
    PersonRef RequestedBy,
    DateTime RequestedAtUtc,
    string Reason,
    PersonRef? DecidedBy,
    DateTime? DecidedAtUtc,
    string? DecisionNotes,
    [property: JsonPropertyName("etag")] string ETag);

/// <summary>An Investigador's request to hand <paramref name="EvidenceCode"/> to another custodian.</summary>
public sealed record TransferRequest(string EvidenceCode, int ToCustodianId, string Reason);

/// <summary>The transfer after the write, and whether it was a replay of one already done with the same key.</summary>
public sealed record TransferOutcome(TransferResource Transfer, bool Replayed);

/// <summary>A field of the request names something that cannot be used; answered as a 400 on that field.</summary>
public sealed class InvalidRequestException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

/// <summary>Other writes to the same evidence kept landing first; nothing was changed, and a retry may succeed.</summary>
public sealed class ConcurrentWriteException(string evidenceCode)
    : Exception($"Other writes to {evidenceCode} kept landing first; nothing was changed. Retry the request.")
{
    public string EvidenceCode { get; } = evidenceCode;
}

/// <summary>Journey 2: request, accept and reject custody transfers, idempotently and with optimistic concurrency.</summary>
public interface ICustodyTransfers
{
    Task<TransferResource?> GetAsync(long transferId, CancellationToken cancellationToken);

    /// <summary>
    /// Requests the transfer once per (requester, key): the same key with the same fingerprint replays the first result;
    /// with another fingerprint it throws <see cref="Idempotency.IdempotencyKeyReusedException"/>.
    /// </summary>
    Task<TransferOutcome> RequestAsync(TransferRequest request, Actor requester, Guid idempotencyKey, byte[] fingerprint, CancellationToken cancellationToken);

    /// <summary>
    /// Accepts or rejects the transfer if it is still at <paramref name="expectedVersion"/>; otherwise throws a conflict
    /// carrying its current state. A repeated key replays the decision it made.
    /// </summary>
    Task<TransferOutcome> DecideAsync(
        long transferId, TransferCommand command, string? notes, Actor decider, byte[] expectedVersion, Guid idempotencyKey, byte[] fingerprint,
        CancellationToken cancellationToken);

    Task<CustodyTransfer?> FindAsync(long transferId, CancellationToken cancellationToken);
}
