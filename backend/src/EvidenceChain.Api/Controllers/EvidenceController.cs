using System.ComponentModel.DataAnnotations;
using EvidenceChain.Api.Inbox;
using EvidenceChain.Application.Inbox;
using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.Custody;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EvidenceChain.Api.Controllers;

/// <summary>Journey 1: find evidence, read its custody and verify its chain. The evidence code is its public id.</summary>
[ApiController]
[Authorize]
[Route("api/v1/evidence")]
public sealed class EvidenceController(IEvidenceInboxQuery inbox) : ControllerBase
{
    private const string NewestFirst = "lastEventAt:desc";
    private const string OldestFirst = "lastEventAt:asc";

    /// <summary>Lists evidence by its latest custody event, one page at a time.</summary>
    /// <param name="q">Code prefix (e.g. EML2026) or text in the description.</param>
    /// <param name="type">LOG, CSV or EML.</param>
    /// <param name="custodianId">Only evidence this user holds now.</param>
    /// <param name="status">Only evidence whose last verification found it Unverified, Valid or Invalid.</param>
    /// <param name="sort">lastEventAt:desc (default) or lastEventAt:asc.</param>
    /// <param name="cursor">The nextCursor of the previous page, sent with the same filters and sort.</param>
    /// <param name="limit">Rows per page, 1 to 50.</param>
    /// <param name="cancellationToken">Aborted when the client cancels.</param>
    [HttpGet]
    [ProducesResponseType<InboxPageResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<InboxPageResponse>> List(
        [FromQuery, StringLength(100)] string? q,
        [FromQuery] string? type,
        [FromQuery, Range(1, int.MaxValue)] int? custodianId,
        [FromQuery] IntegrityStatus? status,
        [FromQuery, AllowedValues(null, NewestFirst, OldestFirst)] string? sort,
        [FromQuery, StringLength(64)] string? cursor,
        [FromQuery, Range(1, 50)] int limit = 25,
        CancellationToken cancellationToken = default)
    {
        if (type is not null && !EvidenceTypes.IsKnown(type))
            ModelState.AddModelError(nameof(type), "Use LOG, CSV or EML.");

        var filter = new EvidenceInboxFilter(string.IsNullOrWhiteSpace(q) ? null : q.Trim(), type, custodianId, status,
            sort == OldestFirst ? InboxSort.OldestFirst : InboxSort.NewestFirst, limit);
        if (cursor is not null)
        {
            if (InboxCursor.TryDecode(cursor, filter, out var after))
                filter = filter with { After = after };
            else
                ModelState.AddModelError(nameof(cursor), "Not a cursor this API issued for these filters and sort; start again without it.");
        }

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var page = await inbox.ListAsync(filter, cancellationToken);
        return Ok(new InboxPageResponse(page.Items, page.Next is { } next ? InboxCursor.Encode(next, filter) : null));
    }
}

/// <summary>A page of the inbox; nextCursor is null on the last page.</summary>
public sealed record InboxPageResponse(IReadOnlyList<EvidenceSummary> Items, string? NextCursor);
