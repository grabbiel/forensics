using System.Net.Http.Json;
using System.Text.Json;
using EvidenceChain.Api.Problems;
using EvidenceChain.IntegrationTests.Probes;

namespace EvidenceChain.IntegrationTests;

/// <summary>Every error a client can act on is problem+json with a stable type (roadmap §3.1).</summary>
[Collection(nameof(SqlCollection))]
public sealed class ProblemDetailsTests(ApiFactory factory)
{
    private const string Rfc403 = "https://tools.ietf.org/html/rfc9110#section-15.5.4";
    private const string Rfc500 = "https://tools.ietf.org/html/rfc9110#section-15.6.1";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Validation_failures_share_one_type_name_the_field_and_carry_a_trace_id()
    {
        var problem = await ProblemAsync("/api/v1/probe/validate?n=9", 400);

        Assert.Equal(ProblemTypes.Validation, problem.GetProperty("type").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("n", out _));
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("pending-unknown", 409, ProblemTypes.InvalidTransition)]
    [InlineData("in-flight", 409, ProblemTypes.IdempotencyInFlight)]
    [InlineData("exhausted", 409, ProblemTypes.DailyIndexExhausted)]
    [InlineData("busy", 409, ProblemTypes.ConcurrentWrite)]
    [InlineData("invalid-field", 400, ProblemTypes.Validation)]
    [InlineData("key-reused", 422, ProblemTypes.IdempotencyKeyReused)]
    [InlineData("not-recipient", 403, Rfc403)]
    [InlineData("role", 403, Rfc403)]
    [InlineData("anything-else", 500, Rfc500)]
    public async Task Domain_and_application_errors_map_to_their_status_and_type(string kind, int status, string type)
    {
        var problem = await ProblemAsync($"/api/v1/probe/throw/{kind}", status);

        Assert.Equal(type, problem.GetProperty("type").GetString());
        Assert.DoesNotContain("secret", problem.GetRawText()); // a 500 says nothing about its cause
    }

    [Theory]
    [InlineData("already-accepted", ProblemTypes.InvalidTransition, "Accepted", "custodio.demo", "2026-10-09T10:00:00Z")]
    [InlineData("stale", ProblemTypes.StaleVersion, "Accepted", "custodio.demo", "2026-10-09T10:00:00Z")]
    [InlineData("pending-exists", ProblemTypes.InvalidTransition, "Pending", "investigador.demo", "2026-10-09T09:00:00Z")] // the open request
    public async Task A_transfer_conflict_says_what_it_is_now_who_acted_when_and_its_etag(string kind, string type, string status, string actedBy, string actedAt)
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? ""); // actedBy is read from the database

        var response = await factory.Probes.CreateClient().GetAsync($"/api/v1/probe/throw/{kind}", Token);
        var problem = await ReadProblemAsync(response, 409);

        Assert.Equal(type, problem.GetProperty("type").GetString());
        Assert.Equal(ProbeController.TransferETag, response.Headers.ETag?.Tag);
        Assert.Equal(ProbeController.TransferETag, problem.GetProperty("currentETag").GetString());

        var state = problem.GetProperty("currentState");
        Assert.Equal((7L, status, 5, 4), (state.GetProperty("transferId").GetInt64(), state.GetProperty("status").GetString(),
            state.GetProperty("fromCustodianId").GetInt32(), state.GetProperty("toCustodianId").GetInt32()));
        Assert.Equal(actedBy, problem.GetProperty("actedBy").GetProperty("userName").GetString());
        Assert.Equal(actedAt, problem.GetProperty("actedAtUtc").GetString());
    }

    private async Task<JsonElement> ProblemAsync(string path, int status) =>
        await ReadProblemAsync(await factory.Probes.CreateClient().GetAsync(path, Token), status);

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response, int status)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        return problem;
    }
}
