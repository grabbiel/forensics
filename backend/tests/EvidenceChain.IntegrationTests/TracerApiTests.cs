using System.Net;

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
    public async Task Invalid_type_returns_problem_json()
    {
        // Validation runs before any SQL, so this needs no database.
        var response = await factory.CreateClientAs(TestUsers.Investigator).GetAsync("/api/v1/evidence?type=PDF", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
