namespace EvidenceChain.IntegrationTests;

[Collection(nameof(SqlCollection))]
public sealed class OpenApiContractTests(ApiFactory factory)
{
    // The brief asks for a published contract that matches the implementation.
    // When this fails, regenerate it with backend/export-openapi.sh and commit the result.
    [Fact]
    public async Task Committed_openapi_yaml_matches_the_running_api()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var live = await factory.CreateClient().GetStringAsync("/openapi/v1.yaml", cancellationToken);
        var committed = await File.ReadAllTextAsync(FindRepoFile("openapi.yaml"), cancellationToken);

        Assert.Equal(Normalize(committed), Normalize(live));
    }

    /// <summary>Walks up from the test output folder to the repository root.</summary>
    private static string FindRepoFile(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, name);
            if (File.Exists(candidate) && Directory.Exists(Path.Combine(dir.FullName, "backend")))
                return candidate;
        }

        throw new FileNotFoundException($"{name} not found above {AppContext.BaseDirectory}.");
    }

    private static string Normalize(string yaml) => yaml.Replace("\r\n", "\n").Trim();
}
