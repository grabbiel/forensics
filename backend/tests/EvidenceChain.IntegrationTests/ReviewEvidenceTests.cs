using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EvidenceChain.Api.Problems;
using EvidenceChain.Application.Integrity;
using EvidenceChain.Domain.Integrity;
using EvidenceChain.Application.Telemetry;
using EvidenceChain.SyntheticData;
using Microsoft.Extensions.DependencyInjection;

namespace EvidenceChain.IntegrationTests;

/// <summary>Journey 1 over the seeded reference dataset: inbox, detail, timeline and verification (roadmap §3.2).</summary>
[Collection(nameof(SqlCollection))]
public sealed class ReviewEvidenceTests(ApiFactory factory)
{
    private static SyntheticDataset Data => ReferenceData.Dataset.Value;
    private static DatasetFixtures Fixtures => Data.Fixtures;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pages_cover_the_inbox_once_newest_first_and_exactly_reversed_oldest_first()
    {
        var client = await ClientAsync();

        var newest = await WalkAsync(client, "limit=50");
        var oldest = await WalkAsync(client, "limit=50&sort=lastEventAt:asc");

        Assert.Equal(Data.Evidences.Count, newest.Select(Code).Distinct().Count());
        Assert.Equal(newest.Count, newest.Select(Code).Distinct().Count());
        var times = newest.Select(i => i.GetProperty("lastEventAtUtc").GetDateTime()).ToList();
        Assert.Equal(times.OrderDescending(), times);
        Assert.Equal(newest.Select(Code).Reverse(), oldest.Select(Code)); // ties break on the id both ways
    }

    [Theory]
    [InlineData("custodianId=4", "currentCustodian.id", "4")]
    [InlineData("type=EML", "typeCode", "EML")]
    [InlineData("q=log2026", "code", "LOG2026")]
    [InlineData("status=Unverified", "integrityStatus", "Unverified")]
    public async Task Each_filter_keeps_only_matching_rows_across_pages(string query, string property, string expected)
    {
        var items = await WalkAsync(await ClientAsync(), $"{query}&limit=50");

        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.StartsWith(expected, Read(item, property)));
    }

    [Fact]
    public async Task Text_also_matches_inside_descriptions()
    {
        var evidence = Data.Evidences.Single(e => e.Code == Fixtures.LargeEmail);
        var text = evidence.Description[5..25];

        var items = await WalkAsync(await ClientAsync(), $"q={Uri.EscapeDataString(text)}&limit=50");

        Assert.Contains(items, item => Code(item) == evidence.Code);
        Assert.All(items, item => Assert.Contains(text, item.GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("limit=0", "limit")]
    [InlineData("limit=51", "limit")]
    [InlineData("sort=code", "sort")]
    [InlineData("status=Broken", "status")]
    [InlineData("type=PDF", "type")]
    [InlineData("custodianId=0", "custodianId")]
    [InlineData("cursor=not-a-cursor", "cursor")]
    public async Task Invalid_parameters_are_validation_problems(string query, string field)
    {
        var response = await (await ClientAsync()).GetAsync($"/api/v1/evidence?{query}", Token);

        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(ProblemTypes.Validation, problem.GetProperty("type").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), problem.GetRawText());
    }

    [Fact]
    public async Task A_cursor_only_continues_the_listing_it_came_from()
    {
        var client = await ClientAsync();
        var first = await client.GetFromJsonAsync<JsonElement>("/api/v1/evidence?type=LOG&limit=5", Token);
        var cursor = first.GetProperty("nextCursor").GetString();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/evidence?type=LOG&limit=10&cursor={cursor}", Token)).StatusCode);
        await ProblemAsync(await client.GetAsync($"/api/v1/evidence?type=CSV&limit=5&cursor={cursor}", Token), HttpStatusCode.BadRequest);
        await ProblemAsync(await client.GetAsync($"/api/v1/evidence?type=LOG&sort=lastEventAt:asc&cursor={cursor}", Token), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_detail_shows_the_overdue_transfer_with_its_etag_and_the_content_without_its_bytes()
    {
        var client = await ClientAsync();
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/evidence/{Fixtures.OverdueTransfer.ToLowerInvariant()}", Token);
        var evidence = Data.Evidences.Single(e => e.Code == Fixtures.OverdueTransfer);

        Assert.Equal(evidence.Code, Code(detail));
        Assert.Equal(Convert.ToHexStringLower(evidence.Sha256), detail.GetProperty("content").GetProperty("sha256").GetString());
        Assert.False(detail.GetProperty("content").TryGetProperty("bytes", out _));

        var pending = detail.GetProperty("pendingTransfer");
        Assert.Equal("Pending", pending.GetProperty("status").GetString());
        Assert.Matches("^\"[0-9a-f]{16}\"$", pending.GetProperty("etag").GetString());

        var anomaly = Assert.Single(detail.GetProperty("anomalies").EnumerateArray());
        Assert.Equal(("Overdue", "Medium", pending.GetProperty("transferId").GetInt64()),
            (anomaly.GetProperty("kind").GetString(), anomaly.GetProperty("severity").GetString(), anomaly.GetProperty("transferId").GetInt64()));
        Assert.Equal("Pendiente desde hace 72 h; el plazo de aceptación es 48 h (24 h de retraso).", anomaly.GetProperty("explanation").GetString());
    }

    [Fact]
    public async Task Late_acceptances_are_historical_anomalies_and_an_unknown_code_is_a_404_problem()
    {
        var client = await ClientAsync();
        foreach (var code in Fixtures.AcceptedLate)
        {
            var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/evidence/{code}", Token);
            var anomaly = Assert.Single(detail.GetProperty("anomalies").EnumerateArray());
            Assert.Equal(("AcceptedLate", "Medium"), (anomaly.GetProperty("kind").GetString(), anomaly.GetProperty("severity").GetString()));
        }

        foreach (var path in new[] { "LOG209901010001", "LOG209901010001/chain", "LOG209901010001/chain/verify" })
            await ProblemAsync(await client.GetAsync($"/api/v1/evidence/{path}", Token), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_timeline_lists_every_event_in_order_linked_and_named()
    {
        var chain = await (await ClientAsync()).GetFromJsonAsync<JsonElement>($"/api/v1/evidence/{Fixtures.Intact}/chain", Token);
        var events = chain.GetProperty("events").EnumerateArray().ToList();

        Assert.Equal(Data.Evidences.Single(e => e.Code == Fixtures.Intact).EventCount, events.Count);
        Assert.Equal(Enumerable.Range(1, events.Count), events.Select(e => e.GetProperty("seq").GetInt32()));
        Assert.Equal("EvidenceRegistered", events[0].GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, events[0].GetProperty("prevMac").ValueKind);
        for (var i = 1; i < events.Count; i++)
            Assert.Equal(events[i - 1].GetProperty("mac").GetString(), events[i].GetProperty("prevMac").GetString());
        Assert.All(events, e => Assert.False(string.IsNullOrEmpty(e.GetProperty("actor").GetProperty("displayName").GetString())));
    }

    [Fact]
    public async Task Verifying_an_intact_chain_records_it_as_valid_through_its_last_event()
    {
        var client = await ClientAsync();
        var report = await client.GetFromJsonAsync<JsonElement>($"/api/v1/evidence/{Fixtures.Intact}/chain/verify", Token);
        var eventCount = Data.Evidences.Single(e => e.Code == Fixtures.Intact).EventCount;

        Assert.True(report.GetProperty("valid").GetBoolean());
        Assert.Equal(eventCount, report.GetProperty("verifiedThroughSeq").GetInt32());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("firstInvalid").ValueKind);

        var integrity = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/evidence/{Fixtures.Intact}", Token)).GetProperty("integrity");
        Assert.Equal(("Valid", eventCount), (integrity.GetProperty("status").GetString(), integrity.GetProperty("checkedThroughSeq").GetInt32()));
        var valid = await WalkAsync(client, "status=Valid&limit=50");
        Assert.Contains(valid, item => Code(item) == Fixtures.Intact);
    }

    public static TheoryData<string, string, int> TamperFixtures() => new()
    {
        { Fixtures.EventTampered, "MAC_MISMATCH", Fixtures.EventTamperedSeq },
        { Fixtures.ContentTampered, "CONTENT_HASH_MISMATCH", 1 },
        { Fixtures.CustodianTampered, "CUSTODY_PROJECTION_MISMATCH", Data.Evidences.Single(e => e.Code == Fixtures.CustodianTampered).EventCount },
    };

    [Theory, MemberData(nameof(TamperFixtures))]
    public async Task Verifying_a_tampered_chain_names_the_first_invalid_event_records_it_and_counts_a_fixture_failure(string code, string reason, int seq)
    {
        var api = await factory.ReferenceApiAsync();
        var client = api.CreateClientAs(TestUsers.Supervisor);
        var failures = CountFailures(api.Services);

        var report = await client.GetFromJsonAsync<JsonElement>($"/api/v1/evidence/{code}/chain/verify", Token);

        Assert.False(report.GetProperty("valid").GetBoolean());
        Assert.Equal(seq - 1, report.GetProperty("verifiedThroughSeq").GetInt32());
        var invalid = report.GetProperty("firstInvalid");
        Assert.Equal((seq, reason), (invalid.GetProperty("seq").GetInt32(), invalid.GetProperty("reason").GetString()));
        Assert.False(string.IsNullOrEmpty(invalid.GetProperty("detail").GetString()));

        var chain = await client.GetFromJsonAsync<JsonElement>($"/api/v1/evidence/{code}/chain", Token);
        Assert.Equal(chain.GetProperty("events")[seq - 1].GetProperty("eventId").GetInt64(), invalid.GetProperty("eventId").GetInt64());

        var integrity = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/evidence/{code}", Token)).GetProperty("integrity");
        Assert.Equal(("Invalid", seq - 1), (integrity.GetProperty("status").GetString(), integrity.GetProperty("checkedThroughSeq").GetInt32()));
        Assert.Equal([$"fixture=True reason={reason}"], failures());
    }

    [Fact]
    public async Task A_verdict_is_not_recorded_over_a_grown_chain_or_a_later_check()
    {
        var api = await factory.ReferenceApiAsync();
        await using var scope = api.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IChainVerificationStore>();
        var input = (await store.LoadAsync(Data.Evidences[10].Code, Token))!;
        var (id, count) = (input.Evidence.EvidenceId, input.Evidence.EventCount);
        var valid = ChainVerdict.Valid(count);
        var at = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(await store.RecordAsync(id, count - 1, valid, at, Token)); // verified before an event was appended
        Assert.True(await store.RecordAsync(id, count, valid, at, Token));
        Assert.False(await store.RecordAsync(id, count, ChainVerdict.Fail(ChainFailure.MacMismatch, 2, "x"), at.AddHours(-1), Token)); // older
    }

    [Fact]
    public async Task A_sweep_verifies_the_least_recently_checked_evidence_first()
    {
        var api = await factory.ReferenceApiAsync();
        await using var scope = api.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IChainVerificationStore>();
        var stalest = await store.StalestAsync(5, Token);

        Assert.Equal(5, await scope.ServiceProvider.GetRequiredService<ChainVerificationService>().SweepAsync(5, Token));

        var client = api.CreateClientAs(TestUsers.Investigator);
        foreach (var code in stalest)
        {
            var integrity = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/evidence/{code}", Token)).GetProperty("integrity");
            Assert.NotEqual("Unverified", integrity.GetProperty("status").GetString());
        }
        Assert.Empty((await store.StalestAsync(5, Token)).Intersect(stalest));
    }

    private async Task<HttpClient> ClientAsync() => (await factory.ReferenceApiAsync()).CreateClientAs(TestUsers.Investigator);

    /// <summary>Every item of every page, following nextCursor.</summary>
    private static async Task<List<JsonElement>> WalkAsync(HttpClient client, string query)
    {
        var items = new List<JsonElement>();
        string? cursor = null;
        for (var page = 0; page < 100; page++)
        {
            var path = $"/api/v1/evidence?{query}" + (cursor is null ? "" : $"&cursor={cursor}");
            var body = await client.GetFromJsonAsync<JsonElement>(path, Token);
            items.AddRange(body.GetProperty("items").EnumerateArray());
            cursor = body.GetProperty("nextCursor").GetString();
            if (cursor is null)
                return items;
        }
        throw new InvalidOperationException("The inbox never ended.");
    }

    /// <summary>Starts listening; the returned function lists what was counted since, as "tag=value" pairs.</summary>
    private static Func<string[]> CountFailures(IServiceProvider services)
    {
        var meterFactory = services.GetRequiredService<IMeterFactory>();
        var counted = new List<string>();
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Scope == meterFactory && instrument.Name == EvidenceChainMetrics.VerifyFailures)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            lock (counted)
                counted.Add(string.Join(" ", tags.ToArray().Select(t => $"{t.Key}={t.Value}").Order()));
        });
        listener.Start();
        return () =>
        {
            listener.Dispose();
            lock (counted)
                return [.. counted];
        };
    }

    private static string Code(JsonElement item) => item.GetProperty("code").GetString()!;

    private static string Read(JsonElement item, string path) =>
        path.Split('.').Aggregate(item, (e, name) => e.GetProperty(name)) is var value && value.ValueKind == JsonValueKind.Number
            ? value.GetRawText()
            : path.Split('.').Aggregate(item, (e, name) => e.GetProperty(name)).GetString()!;

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }
}
