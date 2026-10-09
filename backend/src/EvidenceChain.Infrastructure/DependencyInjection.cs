using EvidenceChain.Application.Inbox;
using EvidenceChain.Infrastructure.Inbox;
using EvidenceChain.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace EvidenceChain.Infrastructure;

/// <summary>Registers persistence and query services.</summary>
public static class DependencyInjection
{
    /// <summary>Adds the DbContext and infrastructure implementations of application ports.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<AppDbContext>(options => SqlServerSetup.Configure(options, connectionString));
        services.AddScoped<IEvidenceInboxQuery, EvidenceInboxQuery>();
        return services;
    }
}
