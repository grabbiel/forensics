extern alias seeder;

using DotNet.Testcontainers.Builders;
using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.People;
using EvidenceChain.Infrastructure.Persistence;
using EvidenceChain.IntegrationTests.Probes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using DatabasePreparation = seeder::EvidenceChain.Seeder.DatabasePreparation;

namespace EvidenceChain.IntegrationTests;

/// <summary>One SQL Server container shared by every database test class.</summary>
[CollectionDefinition(nameof(SqlCollection))]
public sealed class SqlCollection : ICollectionFixture<ApiFactory>;

/// <summary>Runs the API against a real SQL Server 2025 container. Without Docker, DB tests skip locally (on CI they fail); the rest still run.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string Image = "mcr.microsoft.com/mssql/server:2025-latest";
    private const string AppLogin = "evidence_app";
    private const string AppPassword = "DevOnly_TestPassw0rd!2026";
    private MsSqlContainer? _sql;
    private WebApplicationFactory<Program>? _probes;

    /// <summary>sa connection to the test database, or null when Docker is unavailable.</summary>
    public string? AdminConnectionString { get; private set; }

    /// <summary>The API's least-privilege connection, as in docker compose.</summary>
    public string? ConnectionString { get; private set; }

    /// <summary>Why database tests are skipped, if they are.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>The same API plus the test-only <see cref="ProbeController"/>; the published contract never sees it.</summary>
    public WebApplicationFactory<Program> Probes => _probes ??= WithWebHostBuilder(builder =>
        builder.ConfigureServices(services => services.AddControllers().AddApplicationPart(typeof(ProbeController).Assembly)));

    /// <summary>Starts SQL Server, then mirrors the compose migrate job: migrate, prepare, insert two rows.</summary>
    public async ValueTask InitializeAsync()
    {
        try
        {
            _sql = new MsSqlBuilder(Image).Build();
            await _sql.StartAsync();
        }
        catch (DockerUnavailableException ex) when (Environment.GetEnvironmentVariable("CI") != "true")
        {
            // Only a missing Docker skips, and never on CI; a broken image or setup must fail loudly.
            SkipReason = $"Docker unavailable: {ex.Message}";
            return;
        }

        AdminConnectionString = new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "EvidenceChainTests" }.ConnectionString;

        await using (var migrator = SqlServerSetup.CreateContext(AdminConnectionString))
            await migrator.Database.MigrateAsync();

        await PrepareAsync();

        await using var db = SqlServerSetup.CreateContext(AdminConnectionString);
        db.Users.AddRange(
            new User(1, "investigador.demo", "Lucía Ferrer", "investigador.demo@example.test", UserRole.Investigador),
            new User(4, "custodio.demo", "Diego Salas", "custodio.demo@example.test", UserRole.Custodio));
        db.Evidence.AddRange(
            TestEvidence(EvidenceTypes.Log, new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc), "Firewall log fw-edge-01"),
            TestEvidence(EvidenceTypes.Eml, new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc), "Email about a pending transfer"));
        await db.SaveChangesAsync();

        ConnectionString = new SqlConnectionStringBuilder(AdminConnectionString) { UserID = AppLogin, Password = AppPassword }.ConnectionString;
    }

    /// <summary>A freshly migrated, empty database on the same server, for tests that need to write freely.</summary>
    public async Task<string> CreateDatabaseAsync(string name, CancellationToken cancellationToken = default)
    {
        var connectionString = new SqlConnectionStringBuilder(AdminConnectionString) { InitialCatalog = name }.ConnectionString;
        await using (var db = SqlServerSetup.CreateContext(connectionString))
            await db.Database.MigrateAsync(cancellationToken);

        // The API's multi-statement reads use SNAPSHOT transactions.
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await DatabasePreparation.EnableSnapshotIsolationAsync(connection, cancellationToken);
        return connectionString;
    }

    /// <summary>Evidence #1 of its type and day, registered by user 1 and held by user 4.</summary>
    private static Evidence TestEvidence(string type, DateTime registeredAtUtc, string description) =>
        new(type, DateOnly.FromDateTime(registeredAtUtc), 1, description, registeredAtUtc.AddHours(-1), registeredAtUtc,
            registeredById: 1, initialCustodianId: 4, new EvidenceContent(System.Text.Encoding.UTF8.GetBytes(description), "text/plain; charset=utf-8"));

    /// <summary>Runs the seeder's prepare step (RCSI, snapshot isolation and the app login) as sa.</summary>
    public Task PrepareAsync(CancellationToken cancellationToken = default) => PrepareAsync(AdminConnectionString!, cancellationToken);

    private static async Task PrepareAsync(string adminConnectionString, CancellationToken cancellationToken)
    {
        await using (var connection = new SqlConnection(adminConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await DatabasePreparation.EnableReadCommittedSnapshotAsync(connection, cancellationToken);
            await DatabasePreparation.EnableSnapshotIsolationAsync(connection, cancellationToken);
            await DatabasePreparation.CreateAppLoginAsync(connection, AppLogin, AppPassword, cancellationToken);
        }

        // ROLLBACK IMMEDIATE may have killed pooled sessions.
        SqlConnection.ClearAllPools();
    }

    /// <summary>Points the API at the container (or at an unused address when Docker is missing); no background sweeps.</summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default",
            ConnectionString ?? "Server=127.0.0.1,1;Database=Unused;User Id=x;Password=x;Encrypt=False;Connect Timeout=1");
        builder.UseSetting("Integrity:Sweep:Enabled", "false");
    }

    /// <summary>Stops the host, then the container.</summary>
    public override async ValueTask DisposeAsync()
    {
        if (_probes is not null)
            await _probes.DisposeAsync();
        await base.DisposeAsync();
        if (_sql is not null)
            await _sql.DisposeAsync();
    }
}
