using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EvidenceChain.Api.Auth;
using EvidenceChain.Api.Problems;
using EvidenceChain.Application.People;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace EvidenceChain.IntegrationTests;

/// <summary>Demo JWT sign-in, secure-by-default authorization, role policies and the recipient check (roadmap §3.1).</summary>
[Collection(nameof(SqlCollection))]
public sealed class AuthTests(ApiFactory factory)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_demo_user_signs_in_and_the_token_carries_who_they_are_and_their_role()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? ""); // the user is read from the database

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/token", new { userName = "custodio.demo" }, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Token);

        Assert.Equal("Bearer", body.GetProperty("tokenType").GetString());
        var user = body.GetProperty("user");
        Assert.Equal((4, "custodio.demo", "Diego Salas", "Custodio"),
            (user.GetProperty("id").GetInt32(), user.GetProperty("userName").GetString(), user.GetProperty("displayName").GetString(), user.GetProperty("role").GetString()));

        var jwt = new JsonWebToken(body.GetProperty("accessToken").GetString());
        Assert.Equal(("HS256", "4", "Custodio"), (jwt.Alg, jwt.Subject, jwt.GetClaim(TokenClaims.Role).Value));
        Assert.Equal(TimeSpan.FromHours(8), jwt.ValidTo - jwt.ValidFrom);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.EncodedToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/evidence", Token)).StatusCode);
    }

    [Fact]
    public async Task Signing_in_as_nobody_is_a_validation_problem_on_the_user_name()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? "");

        foreach (var body in new object[] { new { userName = "nobody" }, new { } })
        {
            var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/token", body, Token);
            var problem = await ProblemAsync(response, HttpStatusCode.BadRequest);
            Assert.Equal(ProblemTypes.Validation, problem.GetProperty("type").GetString());
            Assert.True(problem.GetProperty("errors").TryGetProperty("userName", out _), problem.GetRawText());
        }
    }

    [Fact]
    public async Task Sign_in_accepts_every_json_media_type_the_contract_lists()
    {
        foreach (var mediaType in new[] { "application/json", "text/json", "application/vnd.evidence+json" })
        {
            var content = new StringContent("{}", System.Text.Encoding.UTF8, mediaType);
            var response = await factory.CreateClient().PostAsync("/api/v1/auth/token", content, Token);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); // validated, so understood: not 415
        }
    }

    [Fact]
    public async Task Without_a_valid_token_every_api_route_answers_401_problem()
    {
        var issuer = factory.Services.GetRequiredService<TokenIssuer>();
        var jwt = factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var otherKey = new JwtOptions { SigningKey = Convert.ToBase64String(new byte[32]), Issuer = jwt.Issuer, Audience = jwt.Audience };
        var yesterday = new FixedClock(DateTime.UtcNow.AddDays(-1));
        var valid = issuer.Issue(TestUsers.Investigator).AccessToken;

        string?[] tokens =
        [
            null,
            "not-a-jwt",
            new TokenIssuer(Options.Create(otherKey), TimeProvider.System).Issue(TestUsers.Investigator).AccessToken,
            new TokenIssuer(Options.Create(jwt), yesterday).Issue(TestUsers.Investigator).AccessToken, // expired
            Unsigned(valid),
        ];
        foreach (var token in tokens)
        foreach (var path in new[] { "/api/v1/evidence", "/api/v1/no-such-route" })
        {
            var client = factory.CreateClient();
            if (token is not null)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync(path, Token);
            await ProblemAsync(response, HttpStatusCode.Unauthorized);
            Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        }
    }

    [Fact]
    public async Task Health_sign_in_and_the_contract_are_open_and_unknown_routes_are_404_for_a_signed_in_user()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/v1/health/live", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/openapi/v1.yaml", Token)).StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/auth/token", new { userName = "x" }, Token)).StatusCode);

        await ProblemAsync(await factory.CreateClientAs(TestUsers.Investigator).GetAsync("/api/v1/no-such-route", Token), HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("investigador", "Investigador")]
    [InlineData("custodio", "Custodio")]
    [InlineData("supervisor", "Supervisor")]
    public async Task Each_role_policy_admits_only_its_role(string path, string role)
    {
        foreach (var user in new[] { TestUsers.Investigator, TestUsers.Custodian, TestUsers.Supervisor })
        {
            var response = await factory.Probes.CreateClientAs(user).GetAsync($"/api/v1/probe/auth/{path}", Token);
            if (user.Role.ToString() == role)
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            else
                await ProblemAsync(response, HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Only_the_custodian_a_transfer_was_sent_to_may_decide_it()
    {
        Assert.Equal(HttpStatusCode.NoContent, (await Recipient(TestUsers.Custodian)).StatusCode);
        await ProblemAsync(await Recipient(TestUsers.OtherCustodian), HttpStatusCode.Forbidden);
        await ProblemAsync(await Recipient(TestUsers.Investigator), HttpStatusCode.Forbidden);
        await ProblemAsync(await Recipient(TestUsers.Custodian with { Role = Domain.People.UserRole.Supervisor }), HttpStatusCode.Forbidden);

        Task<HttpResponseMessage> Recipient(UserSummary user) => factory.Probes.CreateClientAs(user).GetAsync("/api/v1/probe/auth/recipient", Token);
    }

    [Fact]
    public async Task The_signed_in_user_reaches_the_domain_as_an_actor()
    {
        var me = await factory.Probes.CreateClientAs(TestUsers.Custodian).GetFromJsonAsync<JsonElement>("/api/v1/probe/auth/me", Token);
        Assert.Equal((4, "Custodio"), (me.GetProperty("userId").GetInt32(), me.GetProperty("role").GetString()));
    }

    /// <summary>The same claims with "alg": "none" and the signature dropped.</summary>
    private static string Unsigned(string token)
    {
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"alg":"none","typ":"JWT"}""")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{header}.{token.Split('.')[1]}.";
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

}
