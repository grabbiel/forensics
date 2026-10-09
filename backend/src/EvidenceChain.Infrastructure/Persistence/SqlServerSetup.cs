using Microsoft.EntityFrameworkCore;

namespace EvidenceChain.Infrastructure.Persistence;

/// <summary>Single place for provider options, so runtime, design time and the seeder generate identical SQL.</summary>
public static class SqlServerSetup
{
    /// <summary>Compatibility level of SQL Server 2025 and current Azure SQL.</summary>
    public const int CompatibilityLevel = 170;

    /// <summary>Configures SQL Server with a pinned compatibility level and transient-fault retries.</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseSqlServer(connectionString, sql => sql
            .UseCompatibilityLevel(CompatibilityLevel)
            .EnableRetryOnFailure()
            .MigrationsHistoryTable("__EFMigrationsHistory", "dbo"));

    /// <summary>Creates a context outside DI (seeder, tests).</summary>
    public static AppDbContext CreateContext(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        Configure(builder, connectionString);
        return new AppDbContext(builder.Options);
    }
}
