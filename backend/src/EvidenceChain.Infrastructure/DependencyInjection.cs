using EvidenceChain.Application.Custody;
using EvidenceChain.Application.Inbox;
using EvidenceChain.Application.Integrity;
using EvidenceChain.Application.Notifications;
using EvidenceChain.Application.People;
using EvidenceChain.Application.Review;
using EvidenceChain.Infrastructure.Custody;
using EvidenceChain.Infrastructure.Inbox;
using EvidenceChain.Infrastructure.Integrity;
using EvidenceChain.Infrastructure.Notifications;
using EvidenceChain.Infrastructure.People;
using EvidenceChain.Infrastructure.Persistence;
using EvidenceChain.Infrastructure.Review;
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
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IEvidenceQueries, EvidenceQueries>();
        services.AddScoped<IChainVerificationStore, ChainVerificationStore>();
        services.AddScoped<ICustodyTransfers, CustodyTransferService>();
        services.AddScoped<INotifications, NotificationStore>();
        services.AddMemoryCache();
        services.AddScoped<PeopleIndexProvider>();
        return services;
    }
}
