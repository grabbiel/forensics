using System.Text;

namespace EvidenceChain.Api.Security;

/// <summary>Blocks the published, development-only secrets from ever running outside Development.</summary>
public static class DevSecretGuard
{
    /// <summary>Marker embedded in every dev-only secret (plain or base64-decoded).</summary>
    public const string Marker = "DEV-ONLY";

    // Marker used in dev-only database passwords.
    private const string PasswordMarker = "DevOnly";

    private static readonly string[] SecretKeys = ["Jwt:SigningKey", "ConnectionStrings:Default"];
    private const string IntegrityKeysSection = "Integrity:Keys";

    /// <summary>Throws when any dev-only secret is configured and the environment is not Development.</summary>
    public static void ThrowIfDevSecretsOutsideDevelopment(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
            return;

        var offending = FindDevSecrets(configuration).ToArray();
        if (offending.Length > 0)
            throw new InvalidOperationException(
                $"Development-only secrets are configured in '{environment.EnvironmentName}': {string.Join(", ", offending)}.");
    }

    /// <summary>Configuration paths whose values are dev-only secrets.</summary>
    public static IEnumerable<string> FindDevSecrets(IConfiguration configuration)
    {
        foreach (var key in SecretKeys)
            if (IsDevValue(configuration[key]))
                yield return key;

        foreach (var key in configuration.GetSection(IntegrityKeysSection).GetChildren())
            if (IsDevValue(key.Value))
                yield return key.Path;
    }

    /// <summary>True when the value, or its base64 decoding, carries a dev-only marker.</summary>
    public static bool IsDevValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;
        if (value.Contains(Marker, StringComparison.OrdinalIgnoreCase) || value.Contains(PasswordMarker, StringComparison.OrdinalIgnoreCase))
            return true;

        var buffer = new byte[value.Length];
        return Convert.TryFromBase64String(value, buffer, out var written)
            && Encoding.UTF8.GetString(buffer, 0, written).Contains(Marker, StringComparison.Ordinal);
    }
}
