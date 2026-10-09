using Microsoft.EntityFrameworkCore.Design;

namespace EvidenceChain.Infrastructure.Persistence;

/// <summary>Design-time factory for `dotnet ef` and migration bundles; never runs in the app.</summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    // Generating migrations needs no live database; bundles override this with --connection.
    private const string Placeholder = "Server=localhost,1433;Database=EvidenceChain;Integrated Security=false;TrustServerCertificate=True";

    /// <summary>Builds a context with the shared provider options.</summary>
    public AppDbContext CreateDbContext(string[] args) =>
        SqlServerSetup.CreateContext(Environment.GetEnvironmentVariable("EVIDENCECHAIN_DESIGN_CONNECTION") ?? Placeholder);
}
