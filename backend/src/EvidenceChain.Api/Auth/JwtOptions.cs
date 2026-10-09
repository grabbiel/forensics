using Microsoft.IdentityModel.Tokens;

namespace EvidenceChain.Api.Auth;

/// <summary>Bound from "Jwt". The signing key is base64; Development has a published one, Azure reads Key Vault.</summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    /// <summary>HS256 needs a key at least as long as its 256-bit output.</summary>
    public const int MinimumKeyBytes = 32;

    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "evidence-chain";

    public string Audience { get; set; } = "evidence-chain-api";

    /// <summary>How long a token stays valid; a demo session lasts a working day.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(8);

    public SymmetricSecurityKey Key() => new(Convert.FromBase64String(SigningKey));

    internal static bool HasUsableKey(JwtOptions options)
    {
        var buffer = new byte[options.SigningKey.Length];
        return Convert.TryFromBase64String(options.SigningKey, buffer, out var written) && written >= MinimumKeyBytes;
    }
}
