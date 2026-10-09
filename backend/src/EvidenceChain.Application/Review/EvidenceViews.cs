using System.Text.Json.Serialization;
using EvidenceChain.Application.Common;
using EvidenceChain.Domain.Anomalies;
using EvidenceChain.Domain.Custody;

namespace EvidenceChain.Application.Review;

/// <summary>What the content is, without its bytes.</summary>
public sealed record ContentSummary(string Sha256, int ByteLength, string MediaType);

/// <summary>The last recorded verification: valid or invalid through a sequence, or never checked.</summary>
public sealed record IntegritySummary(IntegrityStatus Status, DateTime? CheckedAtUtc, int? CheckedThroughSeq);

/// <summary>A transfer, with the ETag that accept and reject send back as If-Match.</summary>
public sealed record TransferView(
    long TransferId,
    TransferStatus Status,
    PersonRef From,
    PersonRef To,
    PersonRef RequestedBy,
    DateTime RequestedAtUtc,
    string Reason,
    [property: JsonPropertyName("etag")] string ETag);

/// <summary>An overdue-transfer finding on one of the evidence's transfers.</summary>
public sealed record AnomalyView(
    long TransferId,
    TransferAnomalyKind Kind,
    AnomalySeverity Severity,
    DateTime RequestedAtUtc,
    long ElapsedSeconds,
    long DeadlineSeconds,
    string Explanation);

public sealed record EvidenceDetail(
    string Code,
    string TypeCode,
    string Description,
    DateTime CapturedAtUtc,
    DateTime RegisteredAtUtc,
    PersonRef RegisteredBy,
    PersonRef InitialCustodian,
    PersonRef CurrentCustodian,
    int EventCount,
    DateTime LastEventAtUtc,
    ContentSummary Content,
    IntegritySummary Integrity,
    TransferView? PendingTransfer,
    IReadOnlyList<AnomalyView> Anomalies);

/// <summary>One custody event as the timeline shows it; MACs in lowercase hex.</summary>
public sealed record ChainEventView(
    long EventId,
    int Seq,
    CustodyEventKind Kind,
    DateTime OccurredAtUtc,
    PersonRef Actor,
    PersonRef? From,
    PersonRef? To,
    long? TransferId,
    string Notes,
    string KeyId,
    string Mac,
    string? PrevMac);

public sealed record EvidenceChainView(string Code, IReadOnlyList<ChainEventView> Events);

/// <summary>Reads for one evidence, by its code.</summary>
public interface IEvidenceQueries
{
    Task<EvidenceDetail?> GetDetailAsync(string code, CancellationToken cancellationToken);

    Task<EvidenceChainView?> GetChainAsync(string code, CancellationToken cancellationToken);
}
