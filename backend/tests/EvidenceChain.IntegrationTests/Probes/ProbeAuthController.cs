using System.ComponentModel.DataAnnotations;
using EvidenceChain.Api.Auth;
using EvidenceChain.Api.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EvidenceChain.IntegrationTests.Probes;

/// <summary>Test-only endpoints behind each policy. Mounted by <see cref="ApiFactory.Probes"/>.</summary>
[ApiController]
[Route("api/v1/probe/auth")]
public sealed class ProbeAuthController(IAuthorizationService authorization) : ControllerBase
{
    [HttpGet("me")]
    public IActionResult Me() => Ok(User.ToActor());

    [HttpGet("investigador"), Authorize(Policy = Policies.Investigador)]
    public IActionResult Investigador() => NoContent();

    [HttpGet("custodio"), Authorize(Policy = Policies.Custodio)]
    public IActionResult Custodio() => NoContent();

    [HttpGet("supervisor"), Authorize(Policy = Policies.Supervisor)]
    public IActionResult Supervisor() => NoContent();

    /// <summary>A write guarded like accept and reject; echoes the headers it was given.</summary>
    [HttpPost("write"), Authorize(Policy = Policies.Custodio), RequireIdempotencyKey, RequireIfMatch]
    public IActionResult Write([FromQuery, Range(1, 5)] int n = 1) =>
        Ok(new { key = HttpContext.IdempotencyKey(), version = Convert.ToHexStringLower(HttpContext.IfMatchVersion()), n });

    /// <summary>Decides the probe transfer, which was sent to custodio.demo.</summary>
    [HttpGet("recipient")]
    public async Task<IActionResult> Recipient()
    {
        var result = await authorization.AuthorizeAsync(User, ProbeController.AcceptedTransfer(), Policies.TransferRecipient);
        return result.Succeeded ? NoContent() : Forbid();
    }
}
