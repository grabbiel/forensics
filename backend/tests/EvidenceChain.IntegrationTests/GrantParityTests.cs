using System.Text.RegularExpressions;

namespace EvidenceChain.IntegrationTests;

/// <summary>
/// The API's database permissions are written twice, for compose and for Azure, and a grant missing from either fails
/// every custody write there at runtime. This compares the two lists so drift fails the build instead.
/// </summary>
public sealed partial class GrantParityTests
{
    [Fact]
    public void Compose_and_Azure_grant_the_api_the_same_permissions()
    {
        var local = Permissions(FindRepoFile("backend/tools/EvidenceChain.Seeder/DatabasePreparation.cs"));
        var azure = Permissions(FindRepoFile("infra/scripts/prepare-database.sh"));

        Assert.Contains("GRANT UPDATE ON dbo.Notifications (ReadAtUtc)", local);
        Assert.Equal(local.Order(StringComparer.Ordinal), azure.Order(StringComparer.Ordinal));
    }

    /// <summary>Each GRANT or DENY as "STATE PERMISSIONS ON dbo.Table (Columns)", whitespace collapsed, principal dropped.</summary>
    private static IReadOnlyList<string> Permissions(string path) =>
        Statement().Matches(File.ReadAllText(path))
            .Select(m => Whitespace().Replace($"{m.Groups["state"]} {m.Groups["permissions"]} ON {m.Groups["table"]}{m.Groups["columns"]}", " ").Trim())
            .ToList();

    [GeneratedRegex(@"\b(?<state>GRANT|DENY) (?<permissions>[A-Z]+(?:, [A-Z]+)*) ON (?<table>dbo\.\w+)(?<columns>\s*\([^)]*\))? TO\b")]
    private static partial Regex Statement();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static string FindRepoFile(string relativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"{relativePath} not found above {AppContext.BaseDirectory}.");
    }
}
