using EvidenceChain.Application.People;
using EvidenceChain.Domain.People;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EvidenceChain.Api.Controllers;

/// <summary>Who can be named in filters and transfers.</summary>
[ApiController]
[Authorize]
[Route("api/v1/people")]
public sealed class PeopleController(IUserDirectory users) : ControllerBase
{
    /// <summary>Lists users by display name, optionally only one role (e.g. the custodians a transfer can go to).</summary>
    /// <param name="role">Investigador, Custodio or Supervisor.</param>
    /// <param name="cancellationToken">Aborted when the client cancels.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<UserSummary>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<UserSummary>>> List([FromQuery] UserRole? role, CancellationToken cancellationToken) =>
        Ok(await users.ListAsync(role, cancellationToken));
}
