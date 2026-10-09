extern alias seeder;

using DotNet.Testcontainers.Builders;
using EvidenceChain.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using DatabasePreparation = seeder::EvidenceChain.Seeder.DatabasePreparation;

namespace EvidenceChain.IntegrationTests;

/// <summary>One SQL Server container shared by every database test class.</summary>
[CollectionDefinition(nameof(SqlCollection))]
public sealed class SqlCollection : ICollectionFixture<ApiFactory>;

/// <summary>Runs the API against a real SQL Server 2025 container. Without Docker, DB tests skip; the rest still run.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string Image = "mcr.microsoft.com/mssql/server:2025-latest";
    private const string AppLogin = "evidence_app";
    private const string AppPassword = "DevOnly_TestPassw0rd!2026";
    private MsSqlContainer? _sql;

    /// <summary>sa connection to the test database, or null when Docker is unavailable.</summary>
    public string? AdminConnectionString { get; private set; }

    /// <summary>The API's least-privilege connection, as in docker compose.</summary>
    public string? ConnectionString { get; private set; }

    /// <summary>Why database tests are skipped, if they are.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>Starts SQL Server, then mirrors the compose migrate job: migrate, prepare, insert two rows.</summary>
    public async ValueTask InitializeAsync()
    {
        try
        {
            _sql = new MsSqlBuilder(Image).Build();
            await _sql.StartAsync();
        }
        catch (DockerUnavailableException ex)
        {
            // Only a missing Docker skips; a broken image or setup must fail loudly.
            SkipReason = $"Docker unavailable: {ex.Message}";
            return;
        }

        AdminConnectionString = new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "EvidenceChainTests" }.ConnectionString;

        await using (var migrator = SqlServerSetup.CreateContext(AdminConnectionString))
            await migrator.Database.MigrateAsync();

        await PrepareAsync();

        await using var db = SqlServerSetup.CreateContext(AdminConnectionString);
        db.Evidence.AddRange(
            new(Domain.Catalog.EvidenceTypes.Log, new DateOnly(2026, 10, 6), 1, "Firewall log fw-edge-01"),
            new(Domain.Catalog.EvidenceTypes.Eml, new DateOnly(2026, 10, 7), 1, "Email about a pending transfer"));
        await db.SaveChangesAsync();

        ConnectionString = new SqlConnectionStringBuilder(AdminConnectionString) { UserID = AppLogin, Password = AppPassword }.ConnectionString;
    }

    /// <summary>Runs the seeder's prepare step (RCSI and the app login) as sa.</summary>
    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        await using (var connection = new SqlConnection(AdminConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await DatabasePreparation.EnableReadCommittedSnapshotAsync(connection, cancellationToken);
            await DatabasePreparation.CreateAppLoginAsync(connection, AppLogin, AppPassword, cancellationToken);
        }

        // ROLLBACK IMMEDIATE may have killed pooled sessions.
        SqlConnection.ClearAllPools();
    }

    /// <summary>Points the API at the container (or at an unused address when Docker is missing).</summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:Default",
            ConnectionString ?? "Server=127.0.0.1,1;Database=Unused;User Id=x;Password=x;Encrypt=False;Connect Timeout=1");

    /// <summary>Stops the host, then the container.</summary>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_sql is not null)
            await _sql.DisposeAsync();
    }
}
