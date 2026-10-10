using IPNetwork = System.Net.IPNetwork;
using Microsoft.AspNetCore.HttpOverrides;

namespace EvidenceChain.Api.Http;

/// <summary>
/// The caller's address and scheme behind the proxies in front of the API (App Service's front ends, nginx in compose).
/// Only proxies inside "ForwardedHeaders:KnownIPNetworks" are believed; with none configured, X-Forwarded-* is ignored.
/// </summary>
public static class ForwardedHeadersSetup
{
    public const string NetworksKey = "ForwardedHeaders:KnownIPNetworks";

    /// <summary>The configured CIDR ranges, parsed at startup so a typo stops the host instead of trusting less or more.</summary>
    public static IPNetwork[] KnownNetworks(IConfiguration configuration) =>
        (configuration.GetSection(NetworksKey).Get<string[]>() ?? [])
        .Select(range => IPNetwork.TryParse(range, out var network)
            ? network
            : throw new InvalidOperationException($"{NetworksKey} has '{range}', which is not a CIDR range such as 10.0.0.0/8."))
        .ToArray();

    /// <summary>
    /// Trusts exactly these networks, never the framework's loopback default. One hop only: the rightmost entry is the
    /// one the trusted proxy appended, and anything left of it came from the caller, who can write anything there.
    /// </summary>
    public static void TrustOnly(ForwardedHeadersOptions options, IReadOnlyCollection<IPNetwork> networks)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var network in networks)
            options.KnownIPNetworks.Add(network);
    }
}
