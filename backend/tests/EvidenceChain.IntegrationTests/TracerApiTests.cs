using System.Net;
using System.Net.Http.Json;
using EvidenceChain.Application.Inbox;

namespace EvidenceChain.IntegrationTests;

[Collection(nameof(SqlCollection))]
public sealed class TracerApiTests(ApiFactory factory)
{
    [Fact]
    public async Task Liveness_responds_without_touching_SQL_and_is_never_cached()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Inbox_returns_newest_first()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? "");

        var rows = await factory.CreateClient().GetFromJsonAsync<EvidenceSummary[]>("/api/v1/evidence", TestContext.Current.CancellationToken);

        Assert.Equal(["EML202610070001", "LOG202610060001"], rows!.Select(r => r.Code));
    }

    [Fact]
    public async Task Inbox_filters_by_code_prefix_and_type()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? "");
        var client = factory.CreateClient();

        var byPrefix = await client.GetFromJsonAsync<EvidenceSummary[]>("/api/v1/evidence?q=log2026", TestContext.Current.CancellationToken);
        var byType = await client.GetFromJsonAsync<EvidenceSummary[]>("/api/v1/evidence?type=EML", TestContext.Current.CancellationToken);

        Assert.Equal(["LOG202610060001"], byPrefix!.Select(r => r.Code));
        Assert.Equal(["EML202610070001"], byType!.Select(r => r.Code));
    }

    [Fact]
    public async Task Invalid_type_returns_problem_json()
    {
        // Validation runs before any SQL, so this needs no database.
        var response = await factory.CreateClient().GetAsync("/api/v1/evidence?type=PDF", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
