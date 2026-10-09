using EvidenceChain.Application.Common;
using EvidenceChain.Application.Custody;
using EvidenceChain.Application.Idempotency;
using EvidenceChain.Application.People;
using EvidenceChain.Domain.Custody;
using EvidenceChain.Infrastructure.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace EvidenceChain.Api.Problems;

/// <summary>
/// Turns the domain and application errors clients can act on into problem details; anything else falls through
/// to the exception middleware as a 500. A filter rather than an IExceptionHandler: the middleware strips ETag
/// from the responses it writes, and a 409 must carry the current one.
/// </summary>
internal sealed class ProblemExceptionFilter(ProblemDetailsFactory problems, IUserDirectory users) : IAsyncExceptionFilter
{
    public async Task OnExceptionAsync(ExceptionContext context)
    {
        var http = context.HttpContext;
        var problem = context.Exception switch
        {
            InvalidTransitionException { Transfer: { } transfer } e =>
                await ConflictAsync(http, ProblemTypes.InvalidTransition, "The transfer's state does not allow this.", e.Message, transfer),
            InvalidTransitionException e => Problem(http, StatusCodes.Status409Conflict, ProblemTypes.InvalidTransition, "The transfer's state does not allow this.", e.Message),
            StaleVersionException e =>
                await ConflictAsync(http, ProblemTypes.StaleVersion, "The transfer changed since it was read.", e.Message, e.Current),
            RoleNotAllowedException or NotRecipientException => Problem(http, StatusCodes.Status403Forbidden, null, null, context.Exception.Message),
            IdempotencyKeyReusedException e => Problem(http, StatusCodes.Status422UnprocessableEntity, ProblemTypes.IdempotencyKeyReused, "Idempotency-Key reused.", e.Message),
            IdempotencyInFlightException e => Problem(http, StatusCodes.Status409Conflict, ProblemTypes.IdempotencyInFlight, "Request still in progress.", e.Message),
            DailyIndexExhaustedException e => Problem(http, StatusCodes.Status409Conflict, ProblemTypes.DailyIndexExhausted, "No evidence codes left for that day.", e.Message),
            _ => null,
        };
        if (problem is null)
            return;

        context.Result = new ObjectResult(problem) { StatusCode = problem.Status, ContentTypes = { "application/problem+json" } };
        context.ExceptionHandled = true;
    }

    /// <summary>Through the factory, so the RFC defaults and the trace id apply as to every other problem.</summary>
    private ProblemDetails Problem(HttpContext http, int status, string? type, string? title, string detail) =>
        problems.CreateProblemDetails(http, status, title, type, detail);

    /// <summary>
    /// 409 with the transfer as it stands, who last acted on it and when, and its ETag (also as a header),
    /// so the client can tell the user what happened and retry against the current version.
    /// </summary>
    private async Task<TransferConflictProblemDetails> ConflictAsync(HttpContext http, string type, string title, string detail, CustodyTransfer transfer)
    {
        var (actorId, actedAtUtc) = transfer.DecidedById is { } decider
            ? (decider, transfer.DecidedAtUtc!.Value)
            : (transfer.RequestedById, transfer.RequestedAtUtc);
        var etag = transfer.RowVersion.Length > 0 ? EntityTags.Format(transfer.RowVersion) : null;
        if (etag is not null)
            http.Response.Headers.ETag = etag;

        var defaults = Problem(http, StatusCodes.Status409Conflict, type, title, detail);
        return new TransferConflictProblemDetails
        {
            Type = defaults.Type,
            Title = defaults.Title,
            Status = defaults.Status,
            Detail = defaults.Detail,
            Instance = defaults.Instance,
            Extensions = defaults.Extensions, // the trace id
            CurrentState = TransferState.From(transfer),
            CurrentETag = etag,
            ActedBy = await users.FindAsync(actorId, http.RequestAborted),
            ActedAtUtc = actedAtUtc,
        };
    }
}
