using System.Net.Http.Json;
using System.Text.Json;

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

    // The committed contract equals the live one (above), so this checks what is published.
    [Theory]
    [InlineData("/api/v1/custody-transfers", false, new[] { "201", "400", "401", "403", "409", "422", "429" })]
    [InlineData("/api/v1/custody-transfers/{id}/accept", true, new[] { "200", "400", "401", "403", "404", "409", "422", "428", "429" })]
    [InlineData("/api/v1/custody-transfers/{id}/reject", true, new[] { "200", "400", "401", "403", "404", "409", "422", "428", "429" })]
    public async Task Every_write_declares_its_headers_and_problem_responses(string path, bool ifMatch, string[] statuses)
    {
        var document = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json", TestContext.Current.CancellationToken);
        var operation = document.GetProperty("paths").GetProperty(path).GetProperty("post");

        var headers = operation.GetProperty("parameters").EnumerateArray().Where(p => p.GetProperty("in").GetString() == "header")
            .Select(p => (p.GetProperty("name").GetString(), p.GetProperty("required").GetBoolean())).ToList();
        Assert.Contains(("Idempotency-Key", true), headers);
        Assert.Equal(ifMatch, headers.Contains(("If-Match", true)));
        Assert.Equal(statuses, operation.GetProperty("responses").EnumerateObject().Select(r => r.Name).Order());
        Assert.EndsWith("TransferConflictProblemDetails", operation.GetProperty("responses").GetProperty("409")
            .GetProperty("content").GetProperty("application/problem+json").GetProperty("schema").GetProperty("$ref").GetString());

        var success = operation.GetProperty("responses").EnumerateObject().Single(r => r.Name.StartsWith('2'));
        var successHeaders = success.Value.GetProperty("headers").EnumerateObject().Select(h => h.Name).Order();
        Assert.Equal(success.Name == "201" ? ["ETag", "Idempotent-Replayed", "Location"] : ["ETag", "Idempotent-Replayed"], successHeaders);
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
