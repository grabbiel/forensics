using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EvidenceChain.IntegrationTests;

/// <summary>Who the API thinks is calling, behind a proxy: rate limits and telemetry rely on it.</summary>
[Collection(nameof(SqlCollection))]
public sealed class ForwardedHeadersTests(ApiFactory factory)
{
    // An IPv4 peer as Kestrel reports it on a dual-mode socket, inside the trusted 10.0.0.0/8.
    private static readonly IPAddress Proxy = IPAddress.Parse("::ffff:10.1.2.3");
    private static readonly IPAddress Caller = IPAddress.Parse("198.51.100.7");

    private WebApplicationFactory<Program> BehindProxies(Warnings? warnings = null) =>
        factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("ForwardedHeaders:KnownIPNetworks:0", "10.0.0.0/8");
            if (warnings is not null)
                b.ConfigureServices(services => services.AddLogging(logging => logging.AddProvider(warnings)));
        });

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
    public async Task An_IPv6_caller_is_read_with_its_port()
    {
        await using var api = BehindProxies();

        var seen = await SendAsync(api, Proxy, "[2001:db8::7]:51234");

        Assert.Equal(IPAddress.Parse("2001:db8::7"), seen.Connection.RemoteIpAddress);
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
    public async Task Forwarding_from_an_untrusted_peer_is_reported_once_naming_the_peer()
    {
        var warnings = new Warnings();
        await using var api = BehindProxies(warnings);

        await SendAsync(api, Proxy, Caller.ToString()); // trusted: nothing to report
        await SendAsync(api, IPAddress.Parse("203.0.113.9"), Caller.ToString());
        await SendAsync(api, IPAddress.Parse("203.0.113.10"), Caller.ToString());

        var warning = Assert.Single(warnings.Messages);
        Assert.Contains("203.0.113.9", warning);
    }

    [Theory]
    [InlineData("::ffff:10.1.2.3")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task Without_trusted_networks_the_headers_change_nothing(string peer)
    {
        var direct = IPAddress.Parse(peer);

        var seen = await SendAsync(factory, direct, Caller.ToString(), "https");

        Assert.Equal(direct, seen.Connection.RemoteIpAddress);
        Assert.Equal("http", seen.Request.Scheme);
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownIPNetworks:0", "10.0.0.0/33", "has '10.0.0.0/33', which is not a network range")]
    [InlineData("ForwardedHeaders:KnownIPNetworks:0", "10.1.2.3/8", "has '10.1.2.3/8', which is not a network range")] // host bits set
    [InlineData("ForwardedHeaders:KnownIPNetworks:0", "0.0.0.0/0", "has '0.0.0.0/0', which is not a network range")]   // everyone
    [InlineData("ForwardedHeaders:KnownIPNetworks", "10.0.0.0/8", "must be a list")] // ForwardedHeaders__KnownIPNetworks without __0
    [InlineData("ForwardedHeaders_Enabled", "true", "ASPNETCORE_FORWARDEDHEADERS_ENABLED trusts every proxy")]
    public void A_setting_that_would_trust_the_wrong_peers_stops_the_host_from_starting(string key, string value, string message)
    {
        using var wrong = factory.WithWebHostBuilder(b => b.UseSetting(key, value));

        var error = Assert.ThrowsAny<Exception>(() => wrong.Services);
        Assert.Contains(message, error.ToString());
    }

    /// <summary>Collects warnings; the API's own categories only.</summary>
    private sealed class Warnings : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName.StartsWith("EvidenceChain", StringComparison.Ordinal) ? this : null);

        public void Dispose() { }

        private sealed class Logger(Warnings? sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => sink is not null && logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                    sink!.Messages.Enqueue(formatter(state, exception));
            }
        }
    }
}
