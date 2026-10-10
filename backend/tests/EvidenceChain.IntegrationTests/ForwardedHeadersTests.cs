using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EvidenceChain.IntegrationTests;

/// <summary>Who the API thinks is calling, behind a proxy: rate limits and telemetry rely on it.</summary>
[Collection(nameof(SqlCollection))]
public sealed class ForwardedHeadersTests(ApiFactory factory)
{
    // An IPv4 peer as Kestrel reports it on a dual-mode socket, inside the trusted 10.0.0.0/8.
    private static readonly IPAddress Proxy = IPAddress.Parse("::ffff:10.1.2.3");
    private static readonly IPAddress Caller = IPAddress.Parse("198.51.100.7");

    private WebApplicationFactory<Program> BehindProxies() =>
        factory.WithWebHostBuilder(b => b.UseSetting("ForwardedHeaders:KnownIPNetworks:0", "10.0.0.0/8"));

    /// <summary>A request as it reaches Kestrel from `peer`; returns the context the API worked with.</summary>
    private static Task<HttpContext> SendAsync(WebApplicationFactory<Program> api, IPAddress peer, string forwardedFor, string? forwardedProto = null) =>
        api.Server.SendAsync(context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/v1/health/live";
            context.Connection.RemoteIpAddress = peer;
            context.Request.Headers["X-Forwarded-For"] = forwardedFor;
            if (forwardedProto is not null)
                context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
        });

    [Fact]
    public async Task A_trusted_proxy_passes_on_the_callers_address_and_scheme()
    {
        await using var api = BehindProxies();

        // App Service's front ends append the caller's address with its port.
        var seen = await SendAsync(api, Proxy, $"{Caller}:51234", "https");

        Assert.Equal(Caller, seen.Connection.RemoteIpAddress);
        Assert.Equal("https", seen.Request.Scheme);
    }

    [Fact]
    public async Task Only_the_entry_the_proxy_appended_counts_so_a_caller_cannot_choose_its_address()
    {
        await using var api = BehindProxies();

        var seen = await SendAsync(api, Proxy, $"8.8.8.8, {Caller}:51234");

        Assert.Equal(Caller, seen.Connection.RemoteIpAddress);
    }

    [Theory]
    [InlineData("203.0.113.9")] // a caller reaching the API directly
    [InlineData("127.0.0.1")]   // loopback, which the framework trusts by default
    [InlineData("::1")]
    public async Task A_peer_outside_the_trusted_networks_cannot_forward(string peer)
    {
        await using var api = BehindProxies();
        var outsider = IPAddress.Parse(peer);

        var seen = await SendAsync(api, outsider, Caller.ToString(), "https");

        Assert.Equal(outsider, seen.Connection.RemoteIpAddress);
        Assert.Equal("http", seen.Request.Scheme);
    }

    [Fact]
    public async Task Without_trusted_networks_the_headers_change_nothing()
    {
        var seen = await SendAsync(factory, Proxy, Caller.ToString(), "https");

        Assert.Equal(Proxy, seen.Connection.RemoteIpAddress);
        Assert.Equal("http", seen.Request.Scheme);
    }

    [Fact]
    public void A_malformed_network_stops_the_host_from_starting()
    {
        using var typo = factory.WithWebHostBuilder(b => b.UseSetting("ForwardedHeaders:KnownIPNetworks:0", "10.0.0.0/33"));

        var error = Assert.ThrowsAny<Exception>(() => typo.Services);
        Assert.Contains("ForwardedHeaders:KnownIPNetworks has '10.0.0.0/33'", error.ToString());
    }
}
