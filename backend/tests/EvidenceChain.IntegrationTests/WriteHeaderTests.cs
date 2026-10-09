using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EvidenceChain.Api.Problems;
using EvidenceChain.IntegrationTests.Probes;

namespace EvidenceChain.IntegrationTests;

/// <summary>Idempotency-Key and If-Match are enforced before the action runs and declared in the contract (roadmap §3.1).</summary>
[Collection(nameof(SqlCollection))]
public sealed class WriteHeaderTests(ApiFactory factory)
{
    private const string Write = "/api/v1/probe/auth/write";
    private const string Key = "0199b5a2-6f3e-7c1a-9d4b-2e8f6a1c3b57";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_write_with_both_headers_reaches_the_action_with_them_parsed()
    {
        var response = await SendAsync(Key, ProbeController.TransferETag);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal((Key, "00000000000007d1"), (body.GetProperty("key").GetString(), body.GetProperty("version").GetString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData(Key + ", " + Key)]
    public async Task Without_one_uuid_idempotency_key_the_write_is_a_validation_problem(string? key)
    {
        var problem = await ProblemAsync(await SendAsync(key, ProbeController.TransferETag, n: 9), HttpStatusCode.BadRequest);

        Assert.Equal(ProblemTypes.Validation, problem.GetProperty("type").GetString());
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Idempotency-Key", out _));
        Assert.True(errors.TryGetProperty("n", out _)); // reported together with the other invalid fields
    }

    [Fact]
    public async Task Without_if_match_the_write_answers_428()
    {
        var problem = await ProblemAsync(await SendAsync(Key, ifMatch: null), HttpStatusCode.PreconditionRequired);
        Assert.Equal(ProblemTypes.PreconditionRequired, problem.GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("*")]
    [InlineData("W/\"00000000000007d1\"")]
    [InlineData("\"00000000000007d1\", \"00000000000007d2\"")]
    public async Task An_if_match_that_is_not_one_strong_etag_of_this_api_is_a_validation_problem(string ifMatch)
    {
        var problem = await ProblemAsync(await SendAsync(Key, ifMatch), HttpStatusCode.BadRequest);
        Assert.True(problem.GetProperty("errors").TryGetProperty("If-Match", out _));
    }

    [Fact]
    public async Task The_contract_declares_the_headers_the_bearer_scheme_and_the_problem_responses()
    {
        var document = await factory.Probes.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json", Token);

        var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal(("http", "bearer", "JWT"), (scheme.GetProperty("type").GetString(), scheme.GetProperty("scheme").GetString(), scheme.GetProperty("bearerFormat").GetString()));

        var write = document.GetProperty("paths").GetProperty(Write).GetProperty("post");
        var headers = write.GetProperty("parameters").EnumerateArray().Where(p => p.GetProperty("in").GetString() == "header")
            .ToDictionary(p => p.GetProperty("name").GetString()!, p => p.GetProperty("required").GetBoolean());
        Assert.Equal(new Dictionary<string, bool> { ["Idempotency-Key"] = true, ["If-Match"] = true }, headers);
        Assert.True(write.GetProperty("security")[0].TryGetProperty("Bearer", out _));
        foreach (var status in new[] { "400", "401", "403", "428" })
            Assert.True(write.GetProperty("responses").GetProperty(status).GetProperty("content").TryGetProperty("application/problem+json", out _), status);

        var signIn = document.GetProperty("paths").GetProperty("/api/v1/auth/token").GetProperty("post");
        Assert.False(signIn.TryGetProperty("security", out _));
        Assert.False(signIn.GetProperty("responses").TryGetProperty("401", out _));
    }

    private Task<HttpResponseMessage> SendAsync(string? key, string? ifMatch, int n = 1)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Write}?n={n}");
        if (key is not null)
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return factory.Probes.CreateClientAs(TestUsers.Custodian).SendAsync(request, Token);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }
}
