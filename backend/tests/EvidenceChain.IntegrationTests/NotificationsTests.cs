using System.Buffers.Binary;
using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EvidenceChain.Application.People;
using EvidenceChain.SyntheticData;
using Microsoft.AspNetCore.Mvc.Testing;
using static EvidenceChain.IntegrationTests.CustodyWrites;

namespace EvidenceChain.IntegrationTests;

/// <summary>
/// Notifications through the API over a writable copy of the reference dataset, served as the app login. Each test
/// works on its own evidence and asserts on its own transfers, or on counts before and after, so tests share the database.
/// </summary>
[Collection(nameof(SqlCollection))]
public sealed class NotificationsTests(ApiFactory factory)
{
    private const string Notifications = "/api/v1/notifications";
    private static readonly UserSummary OtherSupervisor = User("tomas.herrera");
    private static readonly UserSummary[] Everyone = SyntheticPeople.All.Select(u => User(u.UserName)).ToArray();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_rejected_request_tells_each_party_once_and_never_the_one_who_acted()
    {
        var api = await ApiAsync();
        var (code, holder, recipient) = FreeEvidence(50);
        var bystander = Bystander(holder, recipient);

        var (id, etag) = await RequestedAsync(api, code, recipient, "Análisis forense en laboratorio");

        var told = await AboutAsync(api, recipient, id);
        var request = Assert.Single(told);
        Assert.Equal(("TransferRequested", code, JsonValueKind.Null, "Análisis forense en laboratorio"),
            (request.GetProperty("kind").GetString(), request.GetProperty("evidenceCode").GetString(),
             request.GetProperty("readAtUtc").ValueKind, request.GetProperty("reason").GetString()));
        Assert.Equal((TestUsers.Investigator.Id, holder.Id, recipient.Id, "Lucía Ferrer"),
            (Person(request, "requestedBy"), Person(request, "from"), Person(request, "to"),
             request.GetProperty("requestedBy").GetProperty("displayName").GetString()));
        Assert.Equal(["TransferRequested"], await KindsAsync(api, holder, id));
        Assert.Equal(["TransferRequested"], await KindsAsync(api, TestUsers.Supervisor, id));
        Assert.Equal(["TransferRequested"], await KindsAsync(api, OtherSupervisor, id));
        Assert.Empty(await KindsAsync(api, TestUsers.Investigator, id));
        Assert.Empty(await KindsAsync(api, bystander, id));

        var rejected = await DecideAsync(api, recipient, id, "reject", new { reason = "Falta la orden judicial" }, Guid.NewGuid(), etag);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);

        var rejection = Assert.Single(await AboutAsync(api, TestUsers.Investigator, id));
        Assert.Equal(("TransferRejected", "Falta la orden judicial"),
            (rejection.GetProperty("kind").GetString(), rejection.GetProperty("decisionNotes").GetString()));
        Assert.Equal(["TransferRejected", "TransferRequested"], await KindsAsync(api, TestUsers.Supervisor, id));
        Assert.Equal(["TransferRejected", "TransferRequested"], await KindsAsync(api, OtherSupervisor, id));
        Assert.Equal(["TransferRequested"], await KindsAsync(api, recipient, id));
        Assert.Equal(["TransferRequested"], await KindsAsync(api, holder, id));
        Assert.Empty(await KindsAsync(api, bystander, id));
    }

    [Fact]
    public async Task An_accepted_request_tells_the_requester_and_the_supervisors()
    {
        var api = await ApiAsync();
        var (code, holder, recipient) = FreeEvidence(51);
        var (id, etag) = await RequestedAsync(api, code, recipient, "Traslado a laboratorio");

        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(api, recipient, id, "accept", new { notes = "Recibida" }, Guid.NewGuid(), etag)).StatusCode);

        var acceptance = Assert.Single(await AboutAsync(api, TestUsers.Investigator, id));
        Assert.Equal(("TransferAccepted", "Recibida"), (acceptance.GetProperty("kind").GetString(), acceptance.GetProperty("decisionNotes").GetString()));
        Assert.Equal(["TransferAccepted", "TransferRequested"], await KindsAsync(api, TestUsers.Supervisor, id));
        Assert.Equal(["TransferAccepted", "TransferRequested"], await KindsAsync(api, OtherSupervisor, id));
        Assert.Equal(["TransferRequested"], await KindsAsync(api, recipient, id));
        Assert.Equal(["TransferRequested"], await KindsAsync(api, holder, id));
    }

    [Fact]
    public async Task Replayed_and_refused_writes_notify_no_one()
    {
        var api = await ApiAsync();
        var (code, holder, recipient) = FreeEvidence(52);
        var key = Guid.NewGuid();
        var created = await RequestAsync(api, code, recipient, "Peritaje", key);
        var transfer = await created.Content.ReadFromJsonAsync<JsonElement>(Token);
        var (id, etag) = (transfer.GetProperty("transferId").GetInt64(), transfer.GetProperty("etag").GetString()!);
        var before = await UnreadByUserAsync(api);

        Assert.Equal(HttpStatusCode.Created, (await RequestAsync(api, code, recipient, "Peritaje", key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await RequestAsync(api, code, recipient, "Peritaje", Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await RequestAsync(api, code, recipient, "Otro motivo", key)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(api, holder, id, "accept", null, Guid.NewGuid(), etag)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await DecideAsync(api, recipient, id, "accept", null, Guid.NewGuid(), "\"0000000000000001\"")).StatusCode);
        Assert.Equal(before, await UnreadByUserAsync(api));

        var decisionKey = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(api, recipient, id, "accept", null, decisionKey, etag)).StatusCode);
        var decided = await UnreadByUserAsync(api);
        var replay = await DecideAsync(api, recipient, id, "accept", null, decisionKey, etag);
        Assert.Equal((HttpStatusCode.OK, "true"), (replay.StatusCode, replay.Headers.GetValues("Idempotent-Replayed").Single()));
        Assert.Equal(HttpStatusCode.Conflict, (await DecideAsync(api, recipient, id, "reject", new { reason = "Tarde" }, Guid.NewGuid(), etag)).StatusCode);
        Assert.Equal(decided, await UnreadByUserAsync(api));
        Assert.Equal(["TransferAccepted", "TransferRequested"], await KindsAsync(api, TestUsers.Supervisor, id));
    }

    [Fact]
    public async Task A_user_reads_and_marks_only_their_own_notifications()
    {
        var api = await ApiAsync();
        var (code, _, recipient) = FreeEvidence(53);
        var (id, _) = await RequestedAsync(api, code, recipient, "Peritaje");
        var theirs = Assert.Single(await AboutAsync(api, recipient, id)).GetProperty("notificationId").GetInt64();

        var mine = await AllIdsAsync(api, TestUsers.Investigator);
        Assert.DoesNotContain(theirs, mine);
        await AssertNotFoundAsync(await ReadOneAsync(api, TestUsers.Investigator, theirs));
        await AssertNotFoundAsync(await ReadOneAsync(api, TestUsers.Investigator, 999_999_999));
        Assert.Equal(JsonValueKind.Null, Assert.Single(await AboutAsync(api, recipient, id)).GetProperty("readAtUtc").ValueKind);

        // Mark-all names an id, never a user: someone else's id only reaches the caller's own rows.
        Assert.Equal(HttpStatusCode.NoContent, (await api.CreateClientAs(TestUsers.Investigator).PostAsJsonAsync($"{Notifications}/read", new { upToId = theirs }, Token)).StatusCode);
        Assert.Equal(JsonValueKind.Null, Assert.Single(await AboutAsync(api, recipient, id)).GetProperty("readAtUtc").ValueKind);

        // A cursor issued to a Supervisor seeks only within the investigator's own rows.
        var supervisorPage = await PageAsync(api, TestUsers.Supervisor, cursor: null, limit: 1);
        var forged = await PageAsync(api, TestUsers.Investigator, supervisorPage.GetProperty("nextCursor").GetString(), limit: 50);
        Assert.Subset(mine.ToHashSet(), Ids(forged).ToHashSet());
    }

    [Fact]
    public async Task Marking_read_is_idempotent_and_up_to_leaves_newer_ones_unread()
    {
        var api = await ApiAsync();
        foreach (var index in new[] { 54, 55 })
            await RequestedAsync(api, FreeEvidence(index).Code, FreeEvidence(index).Recipient, "Peritaje");
        var seen = Ids(await PageAsync(api, OtherSupervisor, cursor: null, limit: 50))[0];
        // Arrives after the page the Supervisor marks from.
        var (late, _) = await RequestedAsync(api, FreeEvidence(56).Code, FreeEvidence(56).Recipient, "Peritaje");

        var upTo = await api.CreateClientAs(OtherSupervisor).PostAsJsonAsync($"{Notifications}/read", new { upToId = seen }, Token);

        Assert.Equal(HttpStatusCode.NoContent, upTo.StatusCode);
        var items = await AllItemsAsync(api, OtherSupervisor);
        Assert.All(items, i => Assert.Equal(i.GetProperty("notificationId").GetInt64() > seen, i.GetProperty("readAtUtc").ValueKind == JsonValueKind.Null));
        var newer = Assert.Single(items, i => i.GetProperty("readAtUtc").ValueKind == JsonValueKind.Null);
        Assert.Equal(late, newer.GetProperty("transferId").GetInt64());
        Assert.Equal(1, await UnreadAsync(api, OtherSupervisor));

        Assert.Equal(HttpStatusCode.NoContent, (await ReadOneAsync(api, OtherSupervisor, newer.GetProperty("notificationId").GetInt64())).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ReadOneAsync(api, OtherSupervisor, newer.GetProperty("notificationId").GetInt64())).StatusCode);
        Assert.Equal(0, await UnreadAsync(api, OtherSupervisor));
        Assert.Equal(0, (await PageAsync(api, OtherSupervisor, cursor: null, limit: 1)).GetProperty("unreadCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await api.CreateClientAs(OtherSupervisor).PostAsJsonAsync($"{Notifications}/read", new { upToId = 0 }, Token)).StatusCode);
    }

    [Fact]
    public async Task Pages_cover_the_list_once_newest_first()
    {
        var api = await ApiAsync();
        for (var i = 0; i < 3; i++)
            await RequestedAsync(api, FreeEvidence(60 + i).Code, FreeEvidence(60 + i).Recipient, "Peritaje");

        var all = Ids(await PageAsync(api, TestUsers.Supervisor, cursor: null, limit: 50));
        var walked = new List<long>();
        string? cursor = null;
        do
        {
            var page = await PageAsync(api, TestUsers.Supervisor, cursor, limit: 2);
            Assert.InRange(Ids(page).Count, 1, 2);
            walked.AddRange(Ids(page));
            cursor = page.GetProperty("nextCursor").GetString();
        } while (cursor is not null && walked.Count < all.Count);

        Assert.True(all.Count >= 3);
        Assert.Equal(all.Order().Reverse(), walked.Take(all.Count));
        Assert.Equal(walked.Count, walked.Distinct().Count());
    }

    [Theory]
    [InlineData("x")]
    [InlineData("AgAAAAAAAAAB")] // version 2
    [InlineData("AQAAAAAAAAAAAQ")] // a byte too many
    [InlineData("AQAAAAAAAAAA")] // id 0
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 65 characters
    public async Task A_cursor_this_api_did_not_issue_is_a_400_on_cursor(string cursor)
    {
        var api = await ApiAsync();

        var response = await api.CreateClientAs(TestUsers.Supervisor).GetAsync($"{Notifications}?cursor={cursor}", Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.True(problem.GetProperty("errors").TryGetProperty("cursor", out _), problem.GetRawText());
    }

    [Fact]
    public async Task An_issued_cursor_round_trips_to_the_id_below_which_the_next_page_reads()
    {
        var api = await ApiAsync();
        await RequestedAsync(api, FreeEvidence(64).Code, FreeEvidence(64).Recipient, "Peritaje");
        await RequestedAsync(api, FreeEvidence(65).Code, FreeEvidence(65).Recipient, "Peritaje");

        var page = await PageAsync(api, TestUsers.Supervisor, cursor: null, limit: 1);
        Span<byte> bytes = stackalloc byte[9];
        Assert.True(Base64Url.TryDecodeFromChars(page.GetProperty("nextCursor").GetString(), bytes, out var written));
        Assert.Equal((9, (byte)1, Ids(page)[0]), (written, bytes[0], BinaryPrimitives.ReadInt64BigEndian(bytes[1..])));
    }

    private Task<WebApplicationFactory<Program>> ApiAsync() => factory.SeededApiAsync("EvidenceChainNotifications");

    private static UserSummary Bystander(UserSummary holder, UserSummary recipient) =>
        User(SyntheticPeople.WithRole(SyntheticRole.Custodio).First(u => u != holder.UserName && u != recipient.UserName));

    private static int Person(JsonElement item, string role) => item.GetProperty(role).GetProperty("id").GetInt32();

    private static async Task<JsonElement> PageAsync(WebApplicationFactory<Program> api, UserSummary user, string? cursor, int limit)
    {
        var query = cursor is null ? $"?limit={limit}" : $"?limit={limit}&cursor={Uri.EscapeDataString(cursor)}";
        return await api.CreateClientAs(user).GetFromJsonAsync<JsonElement>(Notifications + query, Token);
    }

    private static List<long> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("notificationId").GetInt64()).ToList();

    private static async Task<List<JsonElement>> AllItemsAsync(WebApplicationFactory<Program> api, UserSummary user)
    {
        var items = new List<JsonElement>();
        string? cursor = null;
        do
        {
            var page = await PageAsync(api, user, cursor, limit: 50);
            items.AddRange(page.GetProperty("items").EnumerateArray());
            cursor = page.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        return items;
    }

    private static async Task<List<long>> AllIdsAsync(WebApplicationFactory<Program> api, UserSummary user) =>
        (await AllItemsAsync(api, user)).Select(i => i.GetProperty("notificationId").GetInt64()).ToList();

    /// <summary>The user's notifications about one transfer, newest first.</summary>
    private static async Task<List<JsonElement>> AboutAsync(WebApplicationFactory<Program> api, UserSummary user, long transferId) =>
        (await AllItemsAsync(api, user)).Where(i => i.GetProperty("transferId").GetInt64() == transferId).ToList();

    private static async Task<List<string>> KindsAsync(WebApplicationFactory<Program> api, UserSummary user, long transferId) =>
        (await AboutAsync(api, user, transferId)).Select(i => i.GetProperty("kind").GetString()!).ToList();

    private static async Task<int> UnreadAsync(WebApplicationFactory<Program> api, UserSummary user) =>
        (await api.CreateClientAs(user).GetFromJsonAsync<JsonElement>($"{Notifications}/unread-count", Token)).GetProperty("unreadCount").GetInt32();

    private static async Task<Dictionary<int, int>> UnreadByUserAsync(WebApplicationFactory<Program> api)
    {
        var counts = new Dictionary<int, int>();
        foreach (var user in Everyone)
            counts[user.Id] = await UnreadAsync(api, user);
        return counts;
    }

    private static Task<HttpResponseMessage> ReadOneAsync(WebApplicationFactory<Program> api, UserSummary user, long id) =>
        api.CreateClientAs(user).PostAsync($"{Notifications}/{id}/read", content: null, Token);

    private static async Task AssertNotFoundAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
