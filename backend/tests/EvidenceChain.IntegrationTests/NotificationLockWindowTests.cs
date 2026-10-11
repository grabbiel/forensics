using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static EvidenceChain.IntegrationTests.CustodyWrites;

namespace EvidenceChain.IntegrationTests;

/// <summary>
/// What a write sends while it holds its evidence's update lock: from the UPDLOCK select to the commit. Notifications
/// ride in the last save's batch and the user list is read before the lock, so the count is the same with an empty
/// people cache as with a warm one.
/// </summary>
[Collection(nameof(SqlCollection))]
public sealed class NotificationLockWindowTests(ApiFactory factory)
{
    private const string CommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";
    private const string TransactionCategory = "Microsoft.EntityFrameworkCore.Database.Transaction";

    [Fact]
    public async Task Requests_and_decisions_send_the_same_commands_under_the_lock_with_a_cold_or_warm_cache()
    {
        var (first, _, firstRecipient) = FreeEvidence(40);
        var (second, _, secondRecipient) = FreeEvidence(41);

        var (warm, api) = await LoggedApiAsync();
        await using (api)
        {
            var (coldRequest, firstTransfer) = await warm.UnderLockAsync(() => RequestAsync(api, first, firstRecipient, "Peritaje", Guid.NewGuid()));
            var (warmDecision, _) = await warm.UnderLockAsync(() => DecideAsync(api, firstRecipient, Id(firstTransfer), "accept", null, Guid.NewGuid(), ETag(firstTransfer)));
            var (warmRequest, secondTransfer) = await warm.UnderLockAsync(() => RequestAsync(api, second, secondRecipient, "Peritaje", Guid.NewGuid()));

            var (cold, coldApi) = await LoggedApiAsync();
            await using (coldApi)
            {
                var (coldDecision, _) = await cold.UnderLockAsync(() =>
                    DecideAsync(coldApi, secondRecipient, Id(secondTransfer), "reject", new { reason = "Sin orden judicial" }, Guid.NewGuid(), ETag(secondTransfer)));

                Assert.Equal((7, 7, 4, 4), (coldRequest, warmRequest, coldDecision, warmDecision));
            }
        }
    }

    private static long Id(JsonElement transfer) => transfer.GetProperty("transferId").GetInt64();

    private static string ETag(JsonElement transfer) => transfer.GetProperty("etag").GetString()!;

    /// <summary>A host of its own, so its people cache starts empty, logging every command and transaction step.</summary>
    private async Task<(SqlLog, WebApplicationFactory<Program>)> LoggedApiAsync()
    {
        var log = new SqlLog();
        var api = (await factory.SeededApiAsync("EvidenceChainNotifications")).WithWebHostBuilder(b =>
        {
            b.UseSetting($"Logging:LogLevel:{CommandCategory}", "Information");
            b.UseSetting($"Logging:LogLevel:{TransactionCategory}", "Debug");
            b.ConfigureServices(services => services.AddLogging(logging => logging.AddProvider(log)));
        });
        return (log, api);
    }

    /// <summary>Keeps the commands and transaction steps EF Core logs for one host, in order.</summary>
    private sealed class SqlLog : ILoggerProvider
    {
        private readonly ConcurrentQueue<(bool Command, string Text)> _entries = new();

        /// <summary>Sends one write that must succeed, and counts its commands from the UPDLOCK select until the commit.</summary>
        public async Task<(int UnderLock, JsonElement Body)> UnderLockAsync(Func<Task<HttpResponseMessage>> write)
        {
            _entries.Clear();
            var response = await write();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body.GetRawText());
            var entries = _entries.ToArray();
            var locked = Array.FindIndex(entries, e => e.Command && e.Text.Contains("UPDLOCK"));
            Assert.True(locked >= 0, "the write took its evidence's lock");
            return (entries.Skip(locked).TakeWhile(e => e.Command || !e.Text.Contains("Committing transaction")).Count(e => e.Command), body);
        }

        public ILogger CreateLogger(string categoryName) =>
            new Logger(this, categoryName switch { CommandCategory => true, TransactionCategory => false, _ => null });

        public void Dispose() { }

        private sealed class Logger(SqlLog sink, bool? command) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => command is { } c && logLevel >= (c ? LogLevel.Information : LogLevel.Debug);

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel) && (command == false || eventId.Name?.EndsWith("CommandExecuted") == true))
                    sink._entries.Enqueue((command!.Value, formatter(state, exception)));
            }
        }
    }
}
