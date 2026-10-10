using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EvidenceChain.Api.Problems;
using EvidenceChain.Api.RateLimiting;
using EvidenceChain.Application.Inbox;
using EvidenceChain.Application.People;
using EvidenceChain.Domain.People;
using EvidenceChain.SyntheticData;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EvidenceChain.IntegrationTests;

/// <summary>The limits themselves, on hosts whose limits are tiny; every other test runs with them out of reach.</summary>
[Collection(nameof(SqlCollection))]
public sealed class RateLimitingTests(ApiFactory factory)
{
    private const string SpaOrigin = "https://spa.example.test";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>`api` with 2 requests in every bucket, one more a minute, and one request at a time through each gate.</summary>
    private static WebApplicationFactory<Program> Limited(WebApplicationFactory<Program> api, Action<RateLimitingOptions>? adjust = null) =>
        api.WithWebHostBuilder(b =>
        {
            b.UseSetting("Cors:Origins:0", SpaOrigin);
            b.ConfigureTestServices(services => services.PostConfigure<RateLimitingOptions>(limits =>
            {
                foreach (var bucket in new[] { limits.SignIn, limits.Docs, limits.Reads, limits.Search, limits.Verify, limits.Writes, limits.AllWrites })
                    (bucket.TokenLimit, bucket.TokensPerPeriod, bucket.ReplenishmentPeriod) = (2, 1, TimeSpan.FromMinutes(1));
                foreach (var gate in new[] { limits.SearchesAtOnce, limits.VerificationsAtOnce })
                    gate.PermitLimit = 1;
                adjust?.Invoke(limits);
            }));
        });

    private static Task<HttpResponseMessage> SignInAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/token") { Content = JsonContent.Create(new { userName = TestUsers.Investigator.UserName }) };
        request.Headers.Add("Origin", SpaOrigin);
        return client.SendAsync(request, Token);
    }

    [Fact]
    public async Task Too_many_sign_ins_get_a_429_problem_whose_Retry_After_the_SPA_can_read()
    {
        await using var api = Limited(factory);
        var client = api.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(client)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(client)).StatusCode);
        var refused = await SignInAsync(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal((ProblemTypes.RateLimited, 429), (problem.GetProperty("type").GetString(), problem.GetProperty("status").GetInt32()));
        Assert.True(problem.TryGetProperty("traceId", out _));
        // The next token comes within the minute.
        Assert.InRange(int.Parse(refused.Headers.GetValues("Retry-After").Single()), 1, 60);
        Assert.Equal(SpaOrigin, refused.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("Retry-After", refused.Headers.GetValues("Access-Control-Expose-Headers").SelectMany(v => v.Split(',', StringSplitOptions.TrimEntries)));
    }

    [Fact]
    public async Task Each_client_address_signs_in_from_its_own_bucket_IPv4_however_reported_and_IPv6_by_its_64()
    {
        await using var api = Limited(factory);
        async Task<int> SignInFrom(string address)
        {
            var context = await api.Server.SendAsync(c =>
            {
                c.Request.Method = HttpMethods.Post;
                c.Request.Path = "/api/v1/auth/token";
                c.Request.ContentType = "application/json";
                c.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes($$"""{"userName":"{{TestUsers.Investigator.UserName}}"}"""));
                c.Connection.RemoteIpAddress = IPAddress.Parse(address);
            }, Token);
            return context.Response.StatusCode;
        }

        var statuses = new List<int>();
        foreach (var address in new[] { "::ffff:198.51.100.7", "::ffff:198.51.100.7", "198.51.100.7", "198.51.100.8", "2001:db8::1", "2001:db8::2", "2001:db8::ffff", "2001:db8:0:1::1" })
            statuses.Add(await SignInFrom(address));

        // The same IPv4 address mapped or plain shares a bucket; so does a /64, and the next /64 starts afresh.
        Assert.Equal([200, 200, 429, 200, 200, 200, 429, 200], statuses);
    }

    [Fact]
    public async Task One_user_running_out_leaves_the_others_untouched()
    {
        await using var api = Limited(factory);
        var investigator = api.CreateClientAs(TestUsers.Investigator);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            statuses.Add((await investigator.GetAsync("/api/v1/people", Token)).StatusCode);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.OK, (await api.CreateClientAs(TestUsers.Custodian).GetAsync("/api/v1/people", Token)).StatusCode);
    }

    [Fact]
    public async Task Listing_and_searching_the_inbox_each_have_a_bucket_apart_from_other_reads()
    {
        await using var api = Limited(factory, limits => limits.Search.TokenLimit = 1);
        var supervisor = api.CreateClientAs(TestUsers.Supervisor);
        async Task<HttpStatusCode> Get(string path) => (await supervisor.GetAsync(path, Token)).StatusCode;

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests],
            [await Get("/api/v1/people"), await Get("/api/v1/people"), await Get("/api/v1/people")]);
        Assert.Equal(HttpStatusCode.OK, await Get("/api/v1/evidence?q=firewall"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Get("/api/v1/evidence?q=email"));
        Assert.Equal(HttpStatusCode.OK, await Get("/api/v1/evidence"));
        Assert.Equal(HttpStatusCode.OK, await Get("/api/v1/evidence?q=%20"));             // a blank search lists
        Assert.Equal(HttpStatusCode.TooManyRequests, await Get("/api/v1/evidence?q=%20&q=x")); // so does a blank first q, as MVC binds it
    }

    [Fact]
    public async Task All_users_share_one_write_budget_since_anyone_can_sign_in_as_anyone()
    {
        await using var api = Limited(factory);
        UserSummary[] investigators =
        [
            TestUsers.Investigator,
            new(2, "martin.ochoa", "Martín Ochoa", UserRole.Investigador),
            new(3, "irene.calvo", "Irene Calvo", UserRole.Investigador),
        ];

        var statuses = new List<HttpStatusCode>();
        foreach (var user in investigators) // one write each, without the key a write needs: the limiter answers first
            statuses.Add((await api.CreateClientAs(user).PostAsJsonAsync("/api/v1/custody-transfers", new { }, Token)).StatusCode);

        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task A_caller_over_its_own_write_limit_spends_none_of_the_shared_budget()
    {
        await using var api = Limited(factory, limits => (limits.Writes.TokenLimit, limits.AllWrites.TokenLimit) = (1, 2));
        async Task<HttpStatusCode> WriteAs(UserSummary user) =>
            (await api.CreateClientAs(user).PostAsJsonAsync("/api/v1/custody-transfers", new { }, Token)).StatusCode;

        var looping = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            looping.Add(await WriteAs(TestUsers.Investigator));

        // Refused by its own bucket, the loop left the shared budget's second token for someone else.
        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], looping);
        Assert.Equal(HttpStatusCode.BadRequest, await WriteAs(new UserSummary(2, "martin.ochoa", "Martín Ochoa", UserRole.Investigador)));
    }

    [Fact]
    public async Task A_full_search_gate_answers_at_once_and_costs_the_caller_nothing()
    {
        var inbox = new HeldInbox();
        await using var api = Limited(factory, limits => limits.Search.TokenLimit = 1)
            .WithWebHostBuilder(b => b.ConfigureTestServices(services => services.AddScoped<IEvidenceInboxQuery>(_ => inbox)));
        async Task<HttpResponseMessage> SearchAs(UserSummary user, string q) => await api.CreateClientAs(user).GetAsync($"/api/v1/evidence?q={q}", Token);

        var held = SearchAs(TestUsers.Custodian, HeldInbox.Hold);
        await inbox.Holding.WaitAsync(TimeSpan.FromSeconds(10), Token);

        // The one search allowed at once is running: refused at once, to try again in a second.
        var gateFull = await SearchAs(TestUsers.Supervisor, "firewall");
        Assert.Equal((HttpStatusCode.TooManyRequests, "1"), (gateFull.StatusCode, gateFull.Headers.GetValues("Retry-After").Single()));

        inbox.Release();
        Assert.Equal(HttpStatusCode.OK, (await held).StatusCode);
        // The refusal spent none of the supervisor's one search.
        Assert.Equal(HttpStatusCode.OK, (await SearchAs(TestUsers.Supervisor, "firewall")).StatusCode);
        // Its bucket now empty, it is refused for longer; the gate it passed on the way is free again for others.
        var outOfSearches = await SearchAs(TestUsers.Supervisor, "email");
        Assert.Equal(HttpStatusCode.TooManyRequests, outOfSearches.StatusCode);
        Assert.True(int.Parse(outOfSearches.Headers.GetValues("Retry-After").Single()) > 1);
        Assert.Equal(HttpStatusCode.OK, (await SearchAs(TestUsers.Investigator, "email")).StatusCode);
    }

    /// <summary>An inbox whose search for <see cref="Hold"/> runs until released; every other answer is an empty page.</summary>
    private sealed class HeldInbox : IEvidenceInboxQuery
    {
        public const string Hold = "hold";
        private readonly TaskCompletionSource _holding = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Holding => _holding.Task;

        public void Release() => _released.TrySetResult();

        public async Task<InboxPage> ListAsync(EvidenceInboxFilter filter, CancellationToken cancellationToken)
        {
            if (filter.Query == Hold)
            {
                _holding.TrySetResult();
                await _released.Task.WaitAsync(cancellationToken);
            }
            return new InboxPage([], null, null);
        }
    }

    [Fact]
    public async Task A_request_without_a_token_is_a_401_that_spends_nothing()
    {
        await using var api = Limited(factory, limits => limits.Reads.TokenLimit = 1);
        var anonymous = api.CreateClient();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/people", Token)).StatusCode);
    }

    [Fact]
    public async Task A_user_signed_in_from_another_network_has_a_budget_of_its_own_there()
    {
        // Everyone signs in as the same demo personas: draining one from a network must not lock it out for the rest.
        await using var api = Limited(factory);
        var token = api.Services.GetRequiredService<EvidenceChain.Api.Auth.TokenIssuer>().Issue(TestUsers.Supervisor).AccessToken;
        async Task<int> ReadFrom(string address) => (await api.Server.SendAsync(c =>
        {
            c.Request.Path = "/api/v1/people";
            c.Request.Headers.Authorization = $"Bearer {token}";
            c.Connection.RemoteIpAddress = IPAddress.Parse(address);
        }, Token)).Response.StatusCode;

        var statuses = new List<int>();
        foreach (var address in new[] { "203.0.113.5", "203.0.113.5", "203.0.113.5", "198.51.100.9" })
            statuses.Add(await ReadFrom(address));

        Assert.Equal([200, 200, 429, 200], statuses);
    }

    [Theory]
    [InlineData("api/v1/evidence", "?q=firewall")]
    [InlineData("api/v1/evidence/{id}/chain/verify", "")]
    public void Searches_and_verifications_are_held_to_one_at_a_time_across_users(string route, string query)
    {
        using var api = Limited(factory);
        var gate = api.Services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.GlobalLimiter!;
        var endpoint = api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().Single(e => e.RoutePattern.RawText == route);
        HttpContext Request(string user)
        {
            var context = new DefaultHttpContext { RequestServices = api.Services };
            context.Request.QueryString = new QueryString(query);
            context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new("sub", user)], "test"));
            context.SetEndpoint(endpoint);
            return context;
        }

        using (var running = gate.AttemptAcquire(Request("1")))
        {
            Assert.True(running.IsAcquired);
            using var another = gate.AttemptAcquire(Request("10"));
            Assert.False(another.IsAcquired);
        }
        using var next = gate.AttemptAcquire(Request("10"));
        Assert.True(next.IsAcquired);

        if (query.Length > 0)
        {
            // Listing without a search is not held back.
            var list = Request("1");
            list.Request.QueryString = QueryString.Empty;
            using var listing = gate.AttemptAcquire(list);
            Assert.True(listing.IsAcquired);
        }
    }

    [Fact]
    public async Task A_throttled_write_saves_nothing_and_its_key_is_a_first_attempt_once_the_wait_is_over()
    {
        // One write per second for the user.
        await using var api = Limited(await factory.SeededApiAsync("EvidenceChainRateLimits"), limits =>
            (limits.Writes.TokenLimit, limits.Writes.ReplenishmentPeriod) = (1, TimeSpan.FromSeconds(1)));
        var (code, recipient) = FreeEvidence();
        var investigator = api.CreateClientAs(TestUsers.Investigator);
        var key = Guid.NewGuid();
        Task<HttpResponseMessage> RequestTransferAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/custody-transfers")
            {
                Content = JsonContent.Create(new { evidenceCode = code, toCustodianId = recipient, reason = "Peritaje externo" }),
            };
            request.Headers.Add("Idempotency-Key", key.ToString());
            return investigator.SendAsync(request, Token);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await investigator.PostAsJsonAsync("/api/v1/custody-transfers", new { }, Token)).StatusCode); // spends the token
        Assert.Equal(HttpStatusCode.TooManyRequests, (await RequestTransferAsync()).StatusCode);

        var detail = await api.CreateClientAs(TestUsers.Supervisor).GetFromJsonAsync<JsonElement>($"/api/v1/evidence/{code}", Token);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("pendingTransfer").ValueKind);

        // Tokens come back on a 100 ms tick, so wait for one rather than for exactly a second.
        var retried = await RequestTransferAsync();
        for (var deadline = DateTime.UtcNow.AddSeconds(5); retried.StatusCode == HttpStatusCode.TooManyRequests && DateTime.UtcNow < deadline;)
        {
            await Task.Delay(100, Token);
            retried = await RequestTransferAsync();
        }
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.False(retried.Headers.Contains("Idempotent-Replayed")); // nothing to replay: this is the first time it ran
    }

    /// <summary>An evidence that is no fixture and has no pending transfer, and a custodian who does not hold it.</summary>
    private static (string Code, int Recipient) FreeEvidence()
    {
        var data = ReferenceData.Dataset.Value;
        var pending = data.Transfers.Where(t => t.Status == SyntheticTransferStatus.Pending).Select(t => t.EvidenceCode).ToHashSet();
        var evidence = data.Evidences.First(e => e.Fixture is null && !pending.Contains(e.Code));
        var recipient = SyntheticPeople.All.First(u => u.Role == SyntheticRole.Custodio && u.UserName != evidence.CurrentCustodian);
        return (evidence.Code, recipient.Id);
    }

    [Fact]
    public async Task The_liveness_check_is_never_limited()
    {
        await using var api = Limited(factory);
        var client = api.CreateClient();

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/health/live", Token)).StatusCode);
    }

    [Fact]
    public void Every_endpoint_names_a_policy_except_the_liveness_check()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        Assert.NotEmpty(endpoints);

        foreach (var endpoint in endpoints)
        {
            var policy = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
            if (endpoint.RoutePattern.RawText == "/api/v1/health/live")
                Assert.True(endpoint.Metadata.GetMetadata<DisableRateLimitingAttribute>() is not null && policy is null, endpoint.DisplayName);
            else
                Assert.True(policy is not null && RateLimitPolicies.All.Contains(policy), $"{endpoint.DisplayName} names no known rate-limit policy");
        }
    }

    [Fact]
    public async Task Turned_off_nothing_is_limited()
    {
        await using var api = Limited(factory, limits => limits.Enabled = false);
        var client = api.CreateClient();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await SignInAsync(client)).StatusCode);
    }

    [Theory]
    [InlineData("RateLimiting:Search:TokenLimit", "0")]
    [InlineData("RateLimiting:Writes:ReplenishmentPeriod", "00:00:00")]
    [InlineData("RateLimiting:SearchesAtOnce:PermitLimit", "0")]
    public void A_limit_no_request_could_pass_stops_the_host_from_starting(string key, string value)
    {
        using var wrong = factory.WithWebHostBuilder(b => b.UseSetting(key, value));

        var error = Assert.ThrowsAny<Exception>(() => wrong.Services);
        Assert.Contains("RateLimiting needs a positive TokenLimit", error.ToString());
    }
}
