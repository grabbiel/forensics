using EvidenceChain.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace EvidenceChain.Infrastructure.Persistence;

/// <summary>EF Core unit of work for the write side.</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Evidence> Evidence => Set<Evidence>();

    /// <summary>Applies every <see cref="IEntityTypeConfiguration{TEntity}"/> in this assembly.</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
