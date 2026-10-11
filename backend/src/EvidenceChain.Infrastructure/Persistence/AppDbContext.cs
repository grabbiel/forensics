using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.Custody;
using EvidenceChain.Domain.Notifications;
using EvidenceChain.Domain.People;
using EvidenceChain.Infrastructure.Persistence.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EvidenceChain.Infrastructure.Persistence;

/// <summary>EF Core unit of work for the write side.</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Evidence> Evidence => Set<Evidence>();

    public DbSet<EvidenceContent> EvidenceContent => Set<EvidenceContent>();

    public DbSet<DailyCounter> DailyCounters => Set<DailyCounter>();

    public DbSet<CustodyEvent> CustodyEvents => Set<CustodyEvent>();

    public DbSet<CustodyTransfer> CustodyTransfers => Set<CustodyTransfer>();

    public DbSet<EvidenceInboxRow> EvidenceInbox => Set<EvidenceInboxRow>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<SeedRun> SeedRuns => Set<SeedRun>();

    /// <summary>Applies every <see cref="IEntityTypeConfiguration{TEntity}"/> in this assembly.</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    /// <summary>Every timestamp is UTC: stored as datetime2 and read back with Kind = Utc.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcConverter>();
    }

    /// <summary>datetime2 has no kind: only UTC goes in, and values come out tagged UTC.</summary>
    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(v => RequireUtc(v), v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
        private static DateTime RequireUtc(DateTime value) =>
            value.Kind == DateTimeKind.Utc ? value : throw new InvalidOperationException($"Only UTC timestamps are stored; got {value.Kind}.");
    }
}
