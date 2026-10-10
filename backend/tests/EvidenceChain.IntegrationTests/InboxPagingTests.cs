using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.Custody;
using EvidenceChain.Domain.People;
using EvidenceChain.Infrastructure.Persistence;
using EvidenceChain.Infrastructure.Persistence.ReadModels;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace EvidenceChain.IntegrationTests;

[Collection(nameof(SqlCollection))]
public sealed class InboxPagingTests(ApiFactory factory)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("limit=25")]
    [InlineData("limit=25&sort=lastEventAt:asc")]
    [InlineData("type=LOG&limit=25")]
    [InlineData("q=log&limit=25&sort=lastEventAt:asc")]
    public async Task Walking_forward_four_pages_and_back_returns_each_page(string query)
    {
        var client = (await factory.ReferenceApiAsync()).CreateClientAs(TestUsers.Investigator);
        var forward = new List<string[]>();
        string? cursor = null;
        JsonElement last = default;
        for (var i = 0; i < 4; i++)
        {
            last = await PageAsync(client, WithCursor(query, cursor));
            if (i == 0)
                Assert.Null(last.GetProperty("prevCursor").GetString());
            forward.Add(Codes(last));
            cursor = last.GetProperty("nextCursor").GetString();
            Assert.NotNull(cursor);
        }

        var back = new List<string[]> { Codes(last) };
        var prev = last.GetProperty("prevCursor").GetString();
        while (prev is not null)
        {
            var page = await PageAsync(client, WithCursor(query, prev));
            Assert.Equal(back[0], Codes(await PageAsync(client, WithCursor(query, page.GetProperty("nextCursor").GetString()))));
            back.Insert(0, Codes(page));
            prev = page.GetProperty("prevCursor").GetString();
        }

        Assert.Equal(forward, back);
    }

    [Fact]
    public async Task Previous_from_page_2_is_the_first_page_and_the_last_page_points_back()
    {
        var client = (await factory.ReferenceApiAsync()).CreateClientAs(TestUsers.Investigator);
        var first = await PageAsync(client, "limit=25");
        Assert.Null(first.GetProperty("prevCursor").GetString());

        var second = await PageAsync(client, WithCursor("limit=25", first.GetProperty("nextCursor").GetString()));
        var back = await PageAsync(client, WithCursor("limit=25", second.GetProperty("prevCursor").GetString()));
        Assert.Equal(Codes(first), Codes(back));
        Assert.Null(back.GetProperty("prevCursor").GetString());
        Assert.Equal(first.GetProperty("nextCursor").GetString(), back.GetProperty("nextCursor").GetString());

        string? cursor = first.GetProperty("nextCursor").GetString();
        JsonElement last = first;
        for (var i = 0; cursor is not null && i < 100; i++)
        {
            last = await PageAsync(client, WithCursor("limit=50", cursor));
            cursor = last.GetProperty("nextCursor").GetString();
        }

        Assert.Null(last.GetProperty("nextCursor").GetString());
        var prev = last.GetProperty("prevCursor").GetString();
        Assert.NotNull(prev);
        var beforeLast = await PageAsync(client, WithCursor("limit=50", prev));
        Assert.Equal(Codes(last), Codes(await PageAsync(client, WithCursor("limit=50", beforeLast.GetProperty("nextCursor").GetString()))));
    }

    [Fact]
    public async Task Rows_that_share_a_timestamp_straddle_a_page_once_in_each_direction()
    {
        await using var api = await TiedApiAsync();
        var client = api.CreateClientAs(TestUsers.Investigator);
        string[] newest =
        [
            "LOG202610010005", "LOG202610010004", "LOG202610010003", "LOG202610010002", "LOG202610010001",
        ];

        Assert.Equal(newest, await WalkAsync(client, "limit=50"));
        await RoundTripAsync(client, "limit=2", newest);
        await RoundTripAsync(client, "limit=2&sort=lastEventAt:asc", newest.Reverse().ToArray());

        var narrow = await PageAsync(client, "limit=1");
        var second = await PageAsync(client, WithCursor("limit=1", narrow.GetProperty("nextCursor").GetString()));
        var widened = await PageAsync(client, WithCursor("limit=2", second.GetProperty("prevCursor").GetString()));
        Assert.Equal(newest[..2], Codes(widened));
        Assert.Null(widened.GetProperty("prevCursor").GetString());
    }

    private async Task<WebApplicationFactory<Program>> TiedApiAsync()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? "");
        var admin = await factory.CreateDatabaseAsync("EvidenceChainInboxTies");
        await using (var db = SqlServerSetup.CreateContext(admin))
        {
            if (!await db.EvidenceInbox.AnyAsync(Token))
            {
                db.Users.AddRange(
                    new User(1, "investigador.demo", "Lucía Ferrer", "investigador.demo@example.test", UserRole.Investigador),
                    new User(4, "custodio.demo", "Diego Salas", "custodio.demo@example.test", UserRole.Custodio));
                var tied = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
                DateTime[] times = [tied.AddHours(-2), tied, tied, tied, tied.AddHours(2)];
                for (var i = 0; i < times.Length; i++)
                {
                    db.Evidence.Add(new Evidence(EvidenceTypes.Log, DateOnly.FromDateTime(times[i]), (short)(i + 1),
                        $"Tied row {i + 1}", times[i].AddMinutes(-1), times[i], registeredById: 1, initialCustodianId: 4,
                        new EvidenceContent(Encoding.UTF8.GetBytes($"row-{i + 1}"), "text/plain")));
                }

                await db.SaveChangesAsync(Token);
                var stored = await db.Evidence.AsNoTracking().OrderBy(e => e.EvidenceId).ToListAsync(Token);
                for (var i = 0; i < stored.Count; i++)
                {
                    db.EvidenceInbox.Add(new EvidenceInboxRow
                    {
                        EvidenceId = stored[i].EvidenceId,
                        Code = stored[i].Code,
                        TypeCode = stored[i].TypeCode,
                        Description = stored[i].Description,
                        CurrentCustodianId = 4,
                        CurrentCustodianName = "Diego Salas",
                        EventCount = 1,
                        LastEventAtUtc = times[i],
                        IntegrityStatus = IntegrityStatus.Unverified,
                    });
                }

                await db.SaveChangesAsync(Token);
            }
        }

        return factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:Default", admin));
    }

    private static async Task RoundTripAsync(HttpClient client, string query, IReadOnlyList<string> expected)
    {
        var forward = await PagesAsync(client, query);
        Assert.Equal(expected, forward.SelectMany(codes => codes));

        var cursor = (await PageAsync(client, query)).GetProperty("nextCursor").GetString();
        JsonElement page = default;
        while (cursor is not null)
        {
            page = await PageAsync(client, WithCursor(query, cursor));
            cursor = page.GetProperty("nextCursor").GetString();
        }

        var back = new List<string[]>();
        cursor = page.GetProperty("prevCursor").GetString();
        back.Add(Codes(page));
        while (cursor is not null)
        {
            page = await PageAsync(client, WithCursor(query, cursor));
            back.Insert(0, Codes(page));
            cursor = page.GetProperty("prevCursor").GetString();
        }

        Assert.Equal(forward, back);
    }

    private static async Task<List<string[]>> PagesAsync(HttpClient client, string query)
    {
        var pages = new List<string[]>();
        string? cursor = null;
        for (var i = 0; i < 20; i++)
        {
            var page = await PageAsync(client, WithCursor(query, cursor));
            pages.Add(Codes(page));
            cursor = page.GetProperty("nextCursor").GetString();
            if (cursor is null)
                return pages;
        }

        throw new InvalidOperationException("The inbox never ended.");
    }

    private static async Task<string[]> WalkAsync(HttpClient client, string query) =>
        (await PagesAsync(client, query)).SelectMany(codes => codes).ToArray();

    private static async Task<JsonElement> PageAsync(HttpClient client, string query) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/v1/evidence?{query}", Token);

    private static string WithCursor(string query, string? cursor) =>
        cursor is null ? query : $"{query}&cursor={Uri.EscapeDataString(cursor)}";

    private static string[] Codes(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("code").GetString()!).ToArray();
}
