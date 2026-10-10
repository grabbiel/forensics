using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EvidenceChain.IntegrationTests;

/// <summary>The user list behind every name in a view is read once, then served from memory.</summary>
[Collection(nameof(SqlCollection))]
public sealed class PeopleCacheTests(ApiFactory factory)
{
    private const string CommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    [Fact]
    public async Task Repeated_page_loads_read_the_user_list_once()
    {
        var sql = new SqlCommands();
        // A host of its own, so its cache starts empty.
        await using var api = (await factory.ReferenceApiAsync()).WithWebHostBuilder(b =>
        {
            b.UseSetting($"Logging:LogLevel:{CommandCategory}", "Information");
            b.ConfigureServices(services => services.AddLogging(logging => logging.AddProvider(sql)));
        });
        var client = api.CreateClientAs(TestUsers.Supervisor);
        var code = ReferenceData.Dataset.Value.Evidences[0].Code;

        foreach (var path in new[] { $"/api/v1/evidence/{code}", $"/api/v1/evidence/{code}/chain", $"/api/v1/evidence/{code}", $"/api/v1/evidence/{code}/chain" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path, TestContext.Current.CancellationToken)).StatusCode);

        // The whole table, as the people index loads it; a lookup of one user reads "SELECT TOP(2) …".
        Assert.Single(sql.Commands, command => command.Contains("FROM [Users]") && !command.Contains("TOP("));
    }

    /// <summary>Keeps the SQL that EF Core logs for one host.</summary>
    private sealed class SqlCommands : ILoggerProvider
    {
        public ConcurrentQueue<string> Commands { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName == CommandCategory ? this : null);

        public void Dispose() { }

        private sealed class Logger(SqlCommands? sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => sink is not null && logLevel >= LogLevel.Information;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                    sink!.Commands.Enqueue(formatter(state, exception));
            }
        }
    }
}
