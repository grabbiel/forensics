using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EvidenceChain.Api.Problems;
using EvidenceChain.Application.People;
using EvidenceChain.Domain.People;
using EvidenceChain.SyntheticData;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EvidenceChain.IntegrationTests;

/// <summary>
/// Journey 2 over a writable copy of the reference dataset, served as the app login (roadmap §3.3), including the §3.4
/// idempotency and concurrency checks. Each test works on its own evidence, one with no pending transfer.
/// </summary>
[Collection(nameof(SqlCollection))]
public sealed class TransferCustodyTests(ApiFactory factory)
{
    private const string Transfers = "/api/v1/custody-transfers";
    private static SyntheticDataset Data => ReferenceData.Dataset.Value;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_request_is_pending_appends_a_signed_event_and_the_chain_still_verifies()
    {
        var api = await ApiAsync();
        var (code, holder, recipient) = FreeEvidence(0);

        var response = await RequestAsync(api, code, recipient, "Análisis forense en laboratorio", Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transfer = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        var id = transfer.GetProperty("transferId").GetInt64();
        Assert.Equal($"{Transfers}/{id}", response.Headers.Location?.AbsolutePath);
        Assert.Equal(transfer.GetProperty("etag").GetString(), response.Headers.ETag?.Tag);
        Assert.Equal(("Pending", holder.Id, recipient.Id, TestUsers.Investigator.Id), (transfer.GetProperty("status").GetString(),
            transfer.GetProperty("from").GetProperty("id").GetInt32(), transfer.GetProperty("to").GetProperty("id").GetInt32(),
            transfer.GetProperty("requestedBy").GetProperty("id").GetInt32()));

        var reader = api.CreateClientAs(TestUsers.Supervisor);
        var detail = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/evidence/{code}", Token);
        Assert.Equal((id, transfer.GetProperty("etag").GetString()),
            (detail.GetProperty("pendingTransfer").GetProperty("transferId").GetInt64(), detail.GetProperty("pendingTransfer").GetProperty("etag").GetString()));
        var last = (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/evidence/{code}/chain", Token)).GetProperty("events").EnumerateArray().Last();
        Assert.Equal(("TransferRequested", id, "Análisis forense en laboratorio"),
            (last.GetProperty("kind").GetString(), last.GetProperty("transferId").GetInt64(), last.GetProperty("notes").GetString()));
        await AssertVerifiesAsync(api, code);

        var fetched = await reader.GetAsync($"{Transfers}/{id}", Token);
        Assert.Equal(transfer.GetProperty("etag").GetString(), fetched.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Resending_a_request_answers_the_same_transfer_and_a_key_reused_for_another_is_422()
    {
        var api = await ApiAsync();
        var (code, _, recipient) = FreeEvidence(1);
        var key = Guid.NewGuid();

        var first = await (await RequestAsync(api, code, recipient, "Peritaje", key)).Content.ReadFromJsonAsync<JsonElement>(Token);
        var again = await RequestAsync(api, code, recipient, "Peritaje", key);

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal("true", again.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(first.GetProperty("transferId").GetInt64(), (await again.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("transferId").GetInt64());

        // The same request spelled differently is still the same request.
        var respelled = await RequestAsync(api, $" {code.ToLowerInvariant()} ", recipient, "  Peritaje ", key);
        Assert.Equal((HttpStatusCode.Created, "true"), (respelled.StatusCode, respelled.Headers.GetValues("Idempotent-Replayed").Single()));

        var reused = await ProblemAsync(await RequestAsync(api, code, recipient, "Otro motivo", key), HttpStatusCode.UnprocessableEntity);
        Assert.Equal(ProblemTypes.IdempotencyKeyReused, reused.GetProperty("type").GetString());

        // A new key while that one is pending: 409 naming it and who asked.
        var conflict = await ProblemAsync(await RequestAsync(api, code, recipient, "Peritaje", Guid.NewGuid()), HttpStatusCode.Conflict);
        Assert.Equal(ProblemTypes.InvalidTransition, conflict.GetProperty("type").GetString());
        Assert.Equal(first.GetProperty("transferId").GetInt64(), conflict.GetProperty("currentState").GetProperty("transferId").GetInt64());
        Assert.Equal("investigador.demo", conflict.GetProperty("actedBy").GetProperty("userName").GetString());
    }

    [Fact]
    public async Task The_same_key_sent_in_parallel_creates_one_transfer_and_every_answer_names_it()
    {
        var api = await ApiAsync();
        var (code, _, recipient) = FreeEvidence(2);
        var key = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => RequestAsync(api, code, recipient, "Copia de seguridad", key)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var ids = await Task.WhenAll(responses.Select(async r => (await r.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("transferId").GetInt64()));
        Assert.Single(ids.Distinct());
        var chain = await api.CreateClientAs(TestUsers.Supervisor).GetFromJsonAsync<JsonElement>($"/api/v1/evidence/{code}/chain", Token);
        Assert.Equal(Data.Evidences.Single(e => e.Code == code).EventCount + 1, chain.GetProperty("events").GetArrayLength()); // one event appended
    }

    [Fact]
    public async Task Requests_name_a_real_evidence_and_another_custodian_carry_a_key_and_come_from_an_investigator()
    {
        var api = await ApiAsync();
        var (code, holder, recipient) = FreeEvidence(3);

        await AssertInvalidAsync(await RequestAsync(api, "LOG209901010001", recipient, "Peritaje", Guid.NewGuid()), "evidenceCode");
        await AssertInvalidAsync(await RequestAsync(api, code, User("supervisor.demo"), "Peritaje", Guid.NewGuid()), "toCustodianId");
        await AssertInvalidAsync(await RequestAsync(api, code, holder, "Peritaje", Guid.NewGuid()), "toCustodianId");
        await AssertInvalidAsync(await RequestAsync(api, code, recipient, "Peritaje", key: null), "Idempotency-Key");
        await AssertInvalidAsync(await RequestAsync(api, code, recipient, "  a  ", Guid.NewGuid()), "reason"); // too short once trimmed
        await ProblemAsync(await RequestAsync(api, code, recipient, "Peritaje", Guid.NewGuid(), as_: holder), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_recipient_accepts_with_the_etag_custody_moves_and_the_chain_still_verifies()
    {
        var api = await ApiAsync();
        var (code, _, recipient) = FreeEvidence(4);
        var requested = await (await RequestAsync(api, code, recipient, "Traslado a laboratorio", Guid.NewGuid())).Content.ReadFromJsonAsync<JsonElement>(Token);
        var (id, etag) = (requested.GetProperty("transferId").GetInt64(), requested.GetProperty("etag").GetString()!);
        var key = Guid.NewGuid();

        var response = await DecideAsync(api, recipient, id, "accept", new { notes = "Recibida y precintada" }, key, etag);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        var newEtag = accepted.GetProperty("etag").GetString();
        Assert.Equal(("Accepted", recipient.Id, "Recibida y precintada"), (accepted.GetProperty("status").GetString(),
            accepted.GetProperty("decidedBy").GetProperty("id").GetInt32(), accepted.GetProperty("decisionNotes").GetString()));
        Assert.NotEqual(etag, newEtag);
        Assert.Equal(newEtag, response.Headers.ETag?.Tag);

        var detail = await api.CreateClientAs(TestUsers.Supervisor).GetFromJsonAsync<JsonElement>($"/api/v1/evidence/{code}", Token);
        Assert.Equal(recipient.Id, detail.GetProperty("currentCustodian").GetProperty("id").GetInt32());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("pendingTransfer").ValueKind);
        await AssertVerifiesAsync(api, code);

        // A retry with the same key replays, even with the ETag it was sent with; a different body is 422.
        var replay = await DecideAsync(api, recipient, id, "accept", new { notes = "Recibida y precintada" }, key, etag);
        Assert.Equal((HttpStatusCode.OK, "true"), (replay.StatusCode, replay.Headers.GetValues("Idempotent-Replayed").Single()));
        Assert.Equal(newEtag, (await replay.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("etag").GetString());
        var respelled = await DecideAsync(api, recipient, id, "accept", new { notes = " Recibida y precintada " }, key, etag.ToUpperInvariant());
        Assert.Equal(HttpStatusCode.OK, respelled.StatusCode);
        await ProblemAsync(await DecideAsync(api, recipient, id, "accept", new { notes = "Otra nota" }, key, etag), HttpStatusCode.UnprocessableEntity);

        // A new key with the old ETag: 409 saying it was accepted, by whom, and the ETag it has now.
        var conflict = await ProblemAsync(await DecideAsync(api, recipient, id, "accept", null, Guid.NewGuid(), etag), HttpStatusCode.Conflict);
        Assert.Equal(("Accepted", newEtag, recipient.UserName), (conflict.GetProperty("currentState").GetProperty("status").GetString(),
            conflict.GetProperty("currentETag").GetString(), conflict.GetProperty("actedBy").GetProperty("userName").GetString()));
    }

    [Fact]
    public async Task Two_accepts_in_parallel_with_one_etag_give_one_200_and_one_409_showing_the_acceptance()
    {
        var api = await ApiAsync();
        var (code, _, recipient) = FreeEvidence(5);
        var requested = await (await RequestAsync(api, code, recipient, "Segunda opinión", Guid.NewGuid())).Content.ReadFromJsonAsync<JsonElement>(Token);
        var (id, etag) = (requested.GetProperty("transferId").GetInt64(), requested.GetProperty("etag").GetString()!);

        // Two tabs of the same recipient, each with its own key.
        var responses = await Task.WhenAll(
            DecideAsync(api, recipient, id, "accept", null, Guid.NewGuid(), etag),
            DecideAsync(api, recipient, id, "accept", null, Guid.NewGuid(), etag));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        var winner = await responses.Single(r => r.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<JsonElement>(Token);
        var loser = await ProblemAsync(responses.Single(r => r.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict);
        Assert.Equal("Accepted", loser.GetProperty("currentState").GetProperty("status").GetString());
        Assert.Equal(winner.GetProperty("etag").GetString(), loser.GetProperty("currentETag").GetString());
        await AssertVerifiesAsync(api, code);
    }

    [Fact]
    public async Task The_same_decision_sent_in_parallel_is_made_once_and_every_answer_names_it()
    {
        var api = await ApiAsync();
        var (code, _, recipient) = FreeEvidence(10);
        var requested = await (await RequestAsync(api, code, recipient, "Reintentos", Guid.NewGuid())).Content.ReadFromJsonAsync<JsonElement>(Token);
        var (id, etag) = (requested.GetProperty("transferId").GetInt64(), requested.GetProperty("etag").GetString()!);
        var key = Guid.NewGuid();

        // One tab retrying the same accept: every answer is 200 with the one acceptance.
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => DecideAsync(api, recipient, id, "accept", null, key, etag)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var etags = await Task.WhenAll(responses.Select(async r => (await r.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("etag").GetString()));
        Assert.Single(etags.Distinct());
        var chain = await api.CreateClientAs(TestUsers.Supervisor).GetFromJsonAsync<JsonElement>($"/api/v1/evidence/{code}/chain", Token);
        Assert.Single(chain.GetProperty("events").EnumerateArray(), e => e.GetProperty("kind").GetString() == "TransferAccepted" && e.GetProperty("transferId").GetInt64() == id);
    }

    [Fact]
    public async Task Only_the_recipient_decides_with_if_match_on_a_transfer_that_exists()
    {
        var api = await ApiAsync();
        var (code, holder, recipient) = FreeEvidence(6);
        var requested = await (await RequestAsync(api, code, recipient, "Revisión", Guid.NewGuid())).Content.ReadFromJsonAsync<JsonElement>(Token);
        var (id, etag) = (requested.GetProperty("transferId").GetInt64(), requested.GetProperty("etag").GetString()!);

        await ProblemAsync(await DecideAsync(api, holder, id, "accept", null, Guid.NewGuid(), etag), HttpStatusCode.Forbidden); // not the recipient
        await ProblemAsync(await DecideAsync(api, TestUsers.Investigator, id, "accept", null, Guid.NewGuid(), etag), HttpStatusCode.Forbidden);
        await ProblemAsync(await DecideAsync(api, recipient, id, "accept", null, Guid.NewGuid(), ifMatch: null), HttpStatusCode.PreconditionRequired);
        await ProblemAsync(await DecideAsync(api, recipient, 999_999, "accept", null, Guid.NewGuid(), etag), HttpStatusCode.NotFound);

        var stale = await ProblemAsync(await DecideAsync(api, recipient, id, "accept", null, Guid.NewGuid(), "\"0000000000000001\""), HttpStatusCode.Conflict);
        Assert.Equal((ProblemTypes.StaleVersion, "Pending", etag), (stale.GetProperty("type").GetString(),
            stale.GetProperty("currentState").GetProperty("status").GetString(), stale.GetProperty("currentETag").GetString()));
    }

    [Fact]
    public async Task A_rejection_needs_a_reason_and_leaves_custody_where_it_was()
    {
        var api = await ApiAsync();
        var (code, holder, recipient) = FreeEvidence(7);
        var requested = await (await RequestAsync(api, code, recipient, "Traslado", Guid.NewGuid())).Content.ReadFromJsonAsync<JsonElement>(Token);
        var (id, etag) = (requested.GetProperty("transferId").GetInt64(), requested.GetProperty("etag").GetString()!);

        await AssertInvalidAsync(await DecideAsync(api, recipient, id, "reject", new { }, Guid.NewGuid(), etag), "reason");
        var response = await DecideAsync(api, recipient, id, "reject", new { reason = "Falta la orden judicial" }, Guid.NewGuid(), etag);

        var rejected = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(("Rejected", "Falta la orden judicial"), (rejected.GetProperty("status").GetString(), rejected.GetProperty("decisionNotes").GetString()));
        var detail = await api.CreateClientAs(TestUsers.Supervisor).GetFromJsonAsync<JsonElement>($"/api/v1/evidence/{code}", Token);
        Assert.Equal(holder.Id, detail.GetProperty("currentCustodian").GetProperty("id").GetInt32());
        await AssertVerifiesAsync(api, code);
    }

    [Fact]
    public async Task A_decision_key_reused_on_another_transfer_is_422()
    {
        var api = await ApiAsync();
        var recipient = User("paula.benitez");
        var (first, _, _) = FreeEvidence(8, avoidHolder: recipient);
        var (second, _, _) = FreeEvidence(9, avoidHolder: recipient);
        var key = Guid.NewGuid();

        foreach (var (code, expected) in new[] { (first, HttpStatusCode.OK), (second, HttpStatusCode.UnprocessableEntity) })
        {
            var requested = await (await RequestAsync(api, code, recipient, "Lote", Guid.NewGuid())).Content.ReadFromJsonAsync<JsonElement>(Token);
            var response = await DecideAsync(api, recipient, requested.GetProperty("transferId").GetInt64(), "accept", null, key, requested.GetProperty("etag").GetString()!);
            Assert.Equal(expected, response.StatusCode);
        }
    }

    private Task<WebApplicationFactory<Program>> ApiAsync() => factory.SeededApiAsync("EvidenceChainTransfers");

    /// <summary>The index-th evidence that is no fixture and has no pending transfer, its holder, and a custodian to send it to.</summary>
    private static (string Code, UserSummary Holder, UserSummary Recipient) FreeEvidence(int index, UserSummary? avoidHolder = null)
    {
        var pending = Data.Transfers.Where(t => t.Status == SyntheticTransferStatus.Pending).Select(t => t.EvidenceCode).ToHashSet();
        var evidence = Data.Evidences
            .Where(e => e.Fixture is null && !pending.Contains(e.Code) && e.CurrentCustodian != avoidHolder?.UserName)
            .ElementAt(index * 7); // spread across types and days
        var holder = User(evidence.CurrentCustodian);
        var recipient = avoidHolder ?? User(SyntheticPeople.WithRole(SyntheticRole.Custodio).First(u => u != holder.UserName));
        return (evidence.Code, holder, recipient);
    }

    private static UserSummary User(string userName)
    {
        var user = SyntheticPeople.All.Single(u => u.UserName == userName);
        return new UserSummary(user.Id, user.UserName, user.DisplayName, Enum.Parse<UserRole>(user.Role.ToString()));
    }

    private static Task<HttpResponseMessage> RequestAsync(
        WebApplicationFactory<Program> api, string code, UserSummary recipient, string reason, Guid? key, UserSummary? as_ = null) =>
        SendAsync(api.CreateClientAs(as_ ?? TestUsers.Investigator), Transfers, new { evidenceCode = code, toCustodianId = recipient.Id, reason }, key, ifMatch: null);

    private static Task<HttpResponseMessage> DecideAsync(
        WebApplicationFactory<Program> api, UserSummary decider, long id, string decision, object? body, Guid key, string? ifMatch) =>
        SendAsync(api.CreateClientAs(decider), $"{Transfers}/{id}/{decision}", body, key, ifMatch);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, object? body, Guid? key, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = body is null ? null : JsonContent.Create(body) };
        if (key is { } k)
            request.Headers.Add("Idempotency-Key", k.ToString());
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(request, Token);
    }

    /// <summary>Signed events written by the API verify like seeded ones, projection included.</summary>
    private static async Task AssertVerifiesAsync(WebApplicationFactory<Program> api, string code)
    {
        var report = await api.CreateClientAs(TestUsers.Supervisor).GetFromJsonAsync<JsonElement>($"/api/v1/evidence/{code}/chain/verify", Token);
        Assert.True(report.GetProperty("valid").GetBoolean(), report.GetRawText());
    }

    private static async Task AssertInvalidAsync(HttpResponseMessage response, string field)
    {
        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(ProblemTypes.Validation, problem.GetProperty("type").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), problem.GetRawText());
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }
}
