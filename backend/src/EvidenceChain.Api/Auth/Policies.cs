using System.Globalization;
using System.Security.Claims;
using EvidenceChain.Application.Custody;
using EvidenceChain.Domain.Custody;
using EvidenceChain.Domain.People;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EvidenceChain.Api.Auth;

/// <summary>Authorization policy names.</summary>
public static class Policies
{
    public const string Investigador = nameof(UserRole.Investigador);
    public const string Custodio = nameof(UserRole.Custodio);
    public const string Supervisor = nameof(UserRole.Supervisor);

    /// <summary>
    /// A Custodio deciding a transfer sent to them. Resource-based: evaluate it against the transfer with
    /// IAuthorizationService.AuthorizeAsync(User, transfer, TransferRecipient); as an attribute it never succeeds.
    /// </summary>
    public const string TransferRecipient = nameof(TransferRecipient);
}

/// <summary>Only the transfer's recipient may accept or reject it.</summary>
public sealed class TransferRecipientRequirement : IAuthorizationRequirement;

/// <summary>Accepts the transfer as the domain holds it or as clients see it.</summary>
internal sealed class TransferRecipientHandler : AuthorizationHandler<TransferRecipientRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TransferRecipientRequirement requirement)
    {
        int? recipient = context.Resource switch
        {
            CustodyTransfer transfer => transfer.ToCustodianId,
            TransferResource transfer => transfer.To.Id,
            _ => null,
        };
        if (recipient is not null && context.User.TryGetUserId(out var userId) && userId == recipient)
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public static class AuthSetup
{
    /// <summary>JWT bearer authentication; every endpoint needs a valid token unless it opts out with AllowAnonymous.</summary>
    public static IServiceCollection AddEvidenceChainAuth(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.Section)
            .Validate(JwtOptions.HasUsableKey, $"Jwt:SigningKey must be base64 of at least {JwtOptions.MinimumKeyBytes} bytes.")
            .Validate(o => o.Lifetime > TimeSpan.Zero, "Jwt:Lifetime must be positive.")
            .ValidateOnStart();
        services.AddSingleton<TokenIssuer>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = jwt.Value.Key(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = TokenClaims.UserName,
                    RoleClaimType = TokenClaims.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddSingleton<IAuthorizationHandler, TransferRecipientHandler>();
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.Investigador, p => p.RequireRole(nameof(UserRole.Investigador)))
            .AddPolicy(Policies.Custodio, p => p.RequireRole(nameof(UserRole.Custodio)))
            .AddPolicy(Policies.Supervisor, p => p.RequireRole(nameof(UserRole.Supervisor)))
            .AddPolicy(Policies.TransferRecipient, p => p
                .RequireRole(nameof(UserRole.Custodio))
                .AddRequirements(new TransferRecipientRequirement()));
        return services;
    }
}

public static class ClaimsPrincipalExtensions
{
    public static bool TryGetUserId(this ClaimsPrincipal user, out int userId) =>
        int.TryParse(user.FindFirstValue(TokenClaims.UserId), NumberStyles.None, CultureInfo.InvariantCulture, out userId);

    /// <summary>The signed-in user as the domain sees them; only call it behind authorization.</summary>
    public static Actor ToActor(this ClaimsPrincipal user) =>
        user.TryGetUserId(out var userId) && Enum.TryParse<UserRole>(user.FindFirstValue(TokenClaims.Role), out var role) && Enum.IsDefined(role)
            ? new Actor(userId, role)
            : throw new InvalidOperationException("The principal carries no user id and role.");
}
