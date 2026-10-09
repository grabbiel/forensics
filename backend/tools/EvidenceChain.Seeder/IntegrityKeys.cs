using System.Text;
using System.Text.Json;
using EvidenceChain.Domain.Integrity;
using Microsoft.Data.SqlClient;

namespace EvidenceChain.Seeder;

/// <summary>
/// Loads the integrity keys the API verifies with: a complete set of environment variables (Azure passes k1 from
/// Key Vault), or, in Development only and when none are set, the API's own appsettings.Development.json.
/// </summary>
internal static class IntegrityKeys
{
    private const string DevSettingsFile = "appsettings.Development.json";
    private const string KeyPrefix = "Integrity__Keys__";

    public static IntegrityKeyRing Load()
    {
        var variables = Environment.GetEnvironmentVariables().Keys.Cast<string>().ToArray();
        var keys = variables.Where(k => k.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(k => k[KeyPrefix.Length..], k => Environment.GetEnvironmentVariable(k)!);
        var active = Environment.GetEnvironmentVariable("Integrity__ActiveKeyId");

        // Any key setting means the operator chose keys: require all of them, never fall back to dev.
        if (active is not null || keys.Count > 0)
        {
            if (active is null || !keys.ContainsKey(active))
                throw new ArgumentException("Set both Integrity__ActiveKeyId and Integrity__Keys__<that id> (base64).");
            return IntegrityKeyRing.FromBase64(active, keys);
        }

        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var file = Path.Combine(AppContext.BaseDirectory, DevSettingsFile);
        if (environment == "Development" && File.Exists(file))
            return FromSettings(file);

        throw new ArgumentException(
            "No integrity keys: set Integrity__ActiveKeyId and Integrity__Keys__<id> (base64), or run with DOTNET_ENVIRONMENT=Development to use the dev key.");
    }

    /// <summary>Refuses the published dev key against Azure SQL, where the API verifies with a Key Vault key.</summary>
    public static void EnsureFitFor(IntegrityKeyRing keys, string connectionString)
    {
        keys.TryGetKey(keys.ActiveKeyId, out var key);
        var isDevKey = Encoding.ASCII.GetString(key).Contains("DEV-ONLY", StringComparison.Ordinal);
        var server = new SqlConnectionStringBuilder(connectionString).DataSource;
        if (isDevKey && server.Contains(".database.windows.net", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The dev-only integrity key cannot sign an Azure SQL database; pass the Key Vault key through Integrity__* variables.");
    }

    /// <summary>Reads Integrity:ActiveKeyId and Integrity:Keys from an appsettings file (comments allowed).</summary>
    private static IntegrityKeyRing FromSettings(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var integrity = document.RootElement.GetProperty("Integrity");
        var keys = integrity.GetProperty("Keys").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        return IntegrityKeyRing.FromBase64(integrity.GetProperty("ActiveKeyId").GetString()!, keys);
    }
}
