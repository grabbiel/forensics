using System.ComponentModel.DataAnnotations;
using EvidenceChain.Application.Inbox;
using EvidenceChain.Domain.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EvidenceChain.Api.Controllers;

/// <summary>Evidence inbox. Tracer endpoint: newest first, no paging yet (keyset arrives on Day 3).</summary>
[ApiController]
[Authorize]
[Route("api/v1/evidence")]
public sealed class EvidenceController(IEvidenceInboxQuery inbox) : ControllerBase
{
    /// <summary>Lists recent evidence.</summary>
    /// <param name="q">Code prefix (e.g. EML2026) or text in the description.</param>
    /// <param name="type">LOG, CSV or EML.</param>
    /// <param name="limit">Rows to return, 1 to 50.</param>
    /// <param name="cancellationToken">Aborted when the client cancels.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<EvidenceSummary>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<EvidenceSummary>>> List(
        [FromQuery, StringLength(100)] string? q,
        [FromQuery] string? type,
        [FromQuery, Range(1, 50)] int limit = 25,
        CancellationToken cancellationToken = default)
    {
        if (type is not null && !EvidenceTypes.IsKnown(type))
        {
            ModelState.AddModelError(nameof(type), "Use LOG, CSV or EML.");
            return ValidationProblem(ModelState);
        }

        var rows = await inbox.ListAsync(new EvidenceInboxFilter(q, type, limit), cancellationToken);
        return Ok(rows);
    }
}
