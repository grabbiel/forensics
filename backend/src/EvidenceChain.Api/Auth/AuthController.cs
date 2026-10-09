using System.ComponentModel.DataAnnotations;
using EvidenceChain.Application.People;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EvidenceChain.Api.Auth;

/// <summary>Demo sign-in.</summary>
[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
public sealed class AuthController(IUserDirectory users, TokenIssuer tokens) : ControllerBase
{
    /// <summary>Signs in as a seeded demo user and returns a bearer token.</summary>
    /// <remarks>Demo sign-in, no password. The role comes from the seeded user, never from the request.</remarks>
    // No param tags: the XML-comment generator would describe the body with the cancellation token's text.
    [HttpPost("token")]
    [Consumes("application/json")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<TokenResponse>> Token(TokenRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByUserNameAsync(request.UserName, cancellationToken);
        if (user is null)
        {
            ModelState.AddModelError("userName", "No demo user has that name.");
            return ValidationProblem(ModelState);
        }

        var token = tokens.Issue(user);
        return Ok(new TokenResponse(token.AccessToken, "Bearer", token.ExpiresAtUtc, user));
    }
}

public sealed class TokenRequest
{
    /// <summary>A seeded user name, e.g. investigador.demo or custodio.demo.</summary>
    [Required, StringLength(64)]
    public string UserName { get; init; } = string.Empty;
}

/// <summary>A bearer token for the Authorization header, when it expires, and who it names.</summary>
public sealed record TokenResponse(string AccessToken, string TokenType, DateTime ExpiresAtUtc, UserSummary User);
