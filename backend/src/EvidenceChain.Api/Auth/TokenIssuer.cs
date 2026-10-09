using System.Globalization;
using EvidenceChain.Application.People;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EvidenceChain.Api.Auth;

/// <summary>Claim names in the tokens this API issues and accepts.</summary>
public static class TokenClaims
{
    public const string UserId = "sub";
    public const string UserName = "preferred_username";
    public const string DisplayName = "name";
    public const string Role = "role";
}

public sealed record IssuedToken(string AccessToken, DateTime ExpiresAtUtc);

/// <summary>Signs HS256 tokens naming a user and their single role.</summary>
public sealed class TokenIssuer(IOptions<JwtOptions> options, TimeProvider clock)
{
    private static readonly JsonWebTokenHandler Handler = new();

    public IssuedToken Issue(UserSummary user)
    {
        var jwt = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now + jwt.Lifetime;
        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(jwt.Key(), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [TokenClaims.UserId] = user.Id.ToString(CultureInfo.InvariantCulture),
                [TokenClaims.UserName] = user.UserName,
                [TokenClaims.DisplayName] = user.DisplayName,
                [TokenClaims.Role] = user.Role.ToString(),
            },
        });
        return new IssuedToken(token, expires);
    }
}
