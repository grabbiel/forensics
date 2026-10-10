using System.ComponentModel.DataAnnotations;
using EvidenceChain.Api.Auth;
using EvidenceChain.Api.Http;
using EvidenceChain.Api.Problems;
using EvidenceChain.Api.RateLimiting;
using EvidenceChain.Application.Custody;
using EvidenceChain.Domain.Custody;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace EvidenceChain.Api.Controllers;

/// <summary>Journey 2: an Investigador asks for a transfer and its recipient accepts or rejects it.</summary>
[ApiController]
[Authorize]
[EnableRateLimiting(RateLimitPolicies.Reads)]
[Route("api/v1/custody-transfers")]
public sealed class CustodyTransfersController(ICustodyTransfers transfers, IAuthorizationService authorization) : ControllerBase
{
    /// <summary>A transfer as it stands now, with its ETag.</summary>
    /// <param name="id">The transfer id.</param>
    /// <param name="cancellationToken">Aborted when the client cancels.</param>
    [HttpGet("{id:long}")]
    [ReturnsETag]
    [ProducesResponseType<TransferResource>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<TransferResource>> Get(long id, CancellationToken cancellationToken)
    {
        if (await transfers.GetAsync(id, cancellationToken) is not { } transfer)
            return NotFoundProblem(id);
        Response.Headers.ETag = transfer.ETag;
        return Ok(transfer);
    }

    /// <summary>Requests that the evidence's current custodian hand it to another custodian.</summary>
    /// <remarks>
    /// Idempotent per Idempotency-Key: resending the same request answers 201 with the transfer as it is now
    /// (Idempotent-Replayed: true); the same key with a different request is 422. An evidence has at most one pending
    /// transfer; a second request is 409 with the one already pending.
    /// </remarks>
    // No param tags: the XML-comment generator would describe the body with the cancellation token's text.
    [HttpPost]
    [Authorize(Policy = Policies.Investigador)]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    [RequireIdempotencyKey]
    [ReturnsETag]
    [ProducesResponseType<TransferResource>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<TransferConflictProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<TransferResource>> Create(RequestTransferBody body, CancellationToken cancellationToken)
    {
        var request = new TransferRequest(body.EvidenceCode.Trim().ToUpperInvariant(), body.ToCustodianId, body.Reason.Trim());
        var outcome = await transfers.RequestAsync(request, User.ToActor(), HttpContext.IdempotencyKey(), RequestFingerprint.Of(HttpContext, request), cancellationToken);
        Describe(outcome);
        return CreatedAtAction(nameof(Get), new { id = outcome.Transfer.TransferId }, outcome.Transfer);
    }

    /// <summary>The recipient takes custody.</summary>
    /// <remarks>
    /// If-Match must carry the ETag the recipient last read: if the transfer has changed since, the answer is 409 with
    /// its current state. Idempotent per Idempotency-Key: resending answers 200 with the decision already made.
    /// </remarks>
    [HttpPost("{id:long}/accept")]
    [Authorize(Policy = Policies.Custodio)]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    [RequireIdempotencyKey]
    [RequireIfMatch]
    [ReturnsETag]
    [ProducesResponseType<TransferResource>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<TransferConflictProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<ActionResult<TransferResource>> Accept(
        long id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AcceptTransferBody? body, CancellationToken cancellationToken) =>
        DecideAsync(id, TransferCommand.Accept, string.IsNullOrWhiteSpace(body?.Notes) ? null : body.Notes.Trim(), cancellationToken);

    /// <summary>The recipient refuses custody; it stays where it was.</summary>
    /// <remarks>Same If-Match and Idempotency-Key rules as accept.</remarks>
    [HttpPost("{id:long}/reject")]
    [Authorize(Policy = Policies.Custodio)]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    [RequireIdempotencyKey]
    [RequireIfMatch]
    [ReturnsETag]
    [ProducesResponseType<TransferResource>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<TransferConflictProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<ActionResult<TransferResource>> Reject(long id, RejectTransferBody body, CancellationToken cancellationToken) =>
        DecideAsync(id, TransferCommand.Reject, body.Reason.Trim(), cancellationToken);

    private async Task<ActionResult<TransferResource>> DecideAsync(long id, TransferCommand command, string? notes, CancellationToken cancellationToken)
    {
        if (await transfers.FindAsync(id, cancellationToken) is not { } transfer)
            return NotFoundProblem(id);
        if (!(await authorization.AuthorizeAsync(User, transfer, Policies.TransferRecipient)).Succeeded)
            return Forbid();

        var outcome = await transfers.DecideAsync(id, command, notes, User.ToActor(), HttpContext.IfMatchVersion(), HttpContext.IdempotencyKey(),
            RequestFingerprint.Of(HttpContext, new { command, notes }), cancellationToken);
        Describe(outcome);
        return Ok(outcome.Transfer);
    }

    /// <summary>The ETag to send back as If-Match, and whether this answer repeats an earlier one.</summary>
    private void Describe(TransferOutcome outcome)
    {
        Response.Headers.ETag = outcome.Transfer.ETag;
        if (outcome.Replayed)
            Response.Headers["Idempotent-Replayed"] = "true";
    }

    private ObjectResult NotFoundProblem(long id) =>
        Problem(statusCode: StatusCodes.Status404NotFound, detail: $"No transfer has the id {id}.");
}

public sealed class RequestTransferBody
{
    /// <summary>The evidence code, e.g. LOG202609110007.</summary>
    [Required, TrimmedLength(15, 15)]
    public string EvidenceCode { get; init; } = string.Empty;

    /// <summary>The Custodio who should receive it; not the one holding it now.</summary>
    [Range(1, int.MaxValue)]
    public int ToCustodianId { get; init; }

    /// <summary>Why it should move: 3 to 500 characters.</summary>
    [Required, TrimmedLength(3, 500)]
    public string Reason { get; init; } = string.Empty;
}

public sealed class AcceptTransferBody
{
    /// <summary>Optional notes on receipt: up to 500 characters.</summary>
    [TrimmedLength(0, 500)]
    public string? Notes { get; init; }
}

public sealed class RejectTransferBody
{
    /// <summary>Why custody is refused: 3 to 500 characters.</summary>
    [Required, TrimmedLength(3, 500)]
    public string Reason { get; init; } = string.Empty;
}
