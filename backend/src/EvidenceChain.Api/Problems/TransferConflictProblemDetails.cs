using EvidenceChain.Application.Custody;
using EvidenceChain.Application.People;
using Microsoft.AspNetCore.Mvc;

namespace EvidenceChain.Api.Problems;

/// <summary>A 409 on a transfer: what it is now, who last acted on it and when, and the ETag to retry with.</summary>
public sealed class TransferConflictProblemDetails : ProblemDetails
{
    public required TransferState CurrentState { get; init; }

    /// <summary>Also sent as the ETag header.</summary>
    public string? CurrentETag { get; init; }

    /// <summary>The decider once decided, otherwise the requester.</summary>
    public UserSummary? ActedBy { get; init; }

    public DateTime ActedAtUtc { get; init; }
}
