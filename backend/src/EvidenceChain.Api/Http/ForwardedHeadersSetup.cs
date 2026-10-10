using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = System.Net.IPNetwork;

namespace EvidenceChain.Api.Http;

/// <summary>
/// The caller's address and scheme behind the proxies in front of the API (App Service's front ends, nginx in compose).
/// Only proxies inside "ForwardedHeaders:KnownIPNetworks" are believed; with none configured, X-Forwarded-* is ignored.
/// </summary>
public static class ForwardedHeadersSetup
{
    public const string NetworksKey = "ForwardedHeaders:KnownIPNetworks";

    /// <summary>
    /// The configured ranges, checked at startup so a mistake stops the host instead of trusting less or more than meant.
    /// The framework's own switch (ASPNETCORE_FORWARDEDHEADERS_ENABLED) is refused: it trusts every peer, and with these
    /// ranges too the headers would be read twice, letting a caller inside a range choose its address.
    /// </summary>
    public static IPNetwork[] KnownNetworks(IConfiguration configuration)
    {
        if (string.Equals(configuration["ForwardedHeaders_Enabled"], "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"ASPNETCORE_FORWARDEDHEADERS_ENABLED trusts every proxy: set it to false and list the proxies' ranges in {NetworksKey}.");

        var section = configuration.GetSection(NetworksKey);
        if (!string.IsNullOrEmpty(section.Value))
            throw new InvalidOperationException($"{NetworksKey} must be a list ({NetworksKey}:0, or ForwardedHeaders__KnownIPNetworks__0 as an environment variable), not one value.");
        return (section.Get<string[]>() ?? []).Select(ParseRange).ToArray();
    }

    /// <summary>A network such as 10.0.0.0/8: never /0, which trusts everyone, nor an address with host bits set.</summary>
    private static IPNetwork ParseRange(string range) =>
        IPNetwork.TryParse(range, out var network) && network.PrefixLength > 0
        && IPAddress.TryParse(range.Split('/')[0], out var address) && address.Equals(network.BaseAddress)
            ? network
            : throw new InvalidOperationException($"{NetworksKey} has '{range}', which is not a network range such as 10.0.0.0/8 (no host bits, not /0).");

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

    /// <summary>
    /// Reads X-Forwarded-* from the trusted proxies, and warns once when a request carried it from a peer outside the
    /// ranges: every caller then looks like that proxy, and limits per address would lump them all together.
    /// </summary>
    public static IApplicationBuilder UseTrustedForwardedHeaders(this IApplicationBuilder app)
    {
        app.UseForwardedHeaders();
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ForwardedHeadersSetup).FullName!);
        var warned = 0;
        return app.Use((context, next) =>
        {
            // The middleware moves what it used to X-Original-For; X-Forwarded-For without it was not believed.
            var headers = context.Request.Headers;
            if (headers.ContainsKey(ForwardedHeadersDefaults.XForwardedForHeaderName)
                && !headers.ContainsKey(ForwardedHeadersDefaults.XOriginalForHeaderName)
                && Interlocked.Exchange(ref warned, 1) == 0)
                logger.LogWarning("X-Forwarded-For came from {Peer}, outside {Setting}: callers are seen as that proxy.", context.Connection.RemoteIpAddress, NetworksKey);
            return next(context);
        });
    }
}
