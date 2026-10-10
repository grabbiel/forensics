using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using EvidenceChain.Api.Auth;
using EvidenceChain.Api.Problems;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace EvidenceChain.Api.RateLimiting;

/// <summary>The endpoint policies; every endpoint names one, except the liveness check.</summary>
public static class RateLimitPolicies
{
    public const string SignIn = "sign-in";
    public const string Docs = "docs";
    public const string Reads = "reads";
    public const string Inbox = "inbox";
    public const string Verify = "verify";
    public const string Writes = "writes";

    public static IReadOnlyList<string> All { get; } = [SignIn, Docs, Reads, Inbox, Verify, Writes];
}

/// <summary>
/// Token buckets per caller (a signed-in user from one client address, or the address alone before sign-in) on each
/// endpoint, then instance-wide limits on the work that costs the database most. All state is in memory: with more than
/// one instance, each counts on its own. A rejection is a 429 problem with Retry-After, sent before the endpoint runs,
/// so a throttled write saved nothing.
/// </summary>
public static class RateLimitingSetup
{
    private static readonly RateLimitPartition<string> NoLimit = RateLimitPartition.GetNoLimiter(string.Empty);

    public static IServiceCollection AddEvidenceChainRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitingOptions>()
            .BindConfiguration(RateLimitingOptions.Section)
            .Validate(RateLimitingOptions.IsValid,
                "RateLimiting needs a positive TokenLimit, TokensPerPeriod and ReplenishmentPeriod on every bucket, and a positive PermitLimit on every gate.")
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests; // the middleware's default is 503
            limiter.OnRejected = RejectAsync;
            // The endpoint's policy name says which limits apply; the global limiter applies them all, in an order that
            // matters, because a refused request has spent whatever the limits before it gave (and the middleware tries
            // twice). A spent token is never given back, a gate's permit is: so the caller's bucket comes before the
            // shared write budget, which a caller over its own limit then cannot drain, and a gate comes before the
            // caller's bucket, so a full gate costs the caller nothing. Gates keep no line, so nothing waits in one.
            foreach (var policy in RateLimitPolicies.All)
                limiter.AddPolicy(policy, _ => NoLimit);
            limiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context => Limits(context).First),
                PartitionedRateLimiter.Create<HttpContext, string>(context => Limits(context).Then));
        });
        return services;
    }

    /// <summary>
    /// The bucket key for a client address: IPv4 as IPv4, however Kestrel reports it, and IPv6 by its /64, the block
    /// one subscriber gets, so a caller cannot step through its own addresses for fresh buckets.
    /// </summary>
    internal static string AddressKey(IPAddress? address)
    {
        if (address is null)
            return "unknown";
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }

    /// <summary>
    /// The validated token's user from one client address. Signing in takes only a name and everyone uses the same demo
    /// personas, so a user alone would let anyone drain a persona's budget for every reviewer signed in as it.
    /// </summary>
    private static string CallerKey(HttpContext context)
    {
        var address = AddressKey(context.Connection.RemoteIpAddress);
        return context.User.FindFirst(TokenClaims.UserId)?.Value is { } id ? $"{id}@{address}" : address;
    }

    // The first value, as MVC binds it: "?q=%20&q=firewall" lists, so it counts as a list.
    private static bool IsSearch(HttpContext context) => !string.IsNullOrWhiteSpace(context.Request.Query["q"].FirstOrDefault());

    private static string? PolicyOf(HttpContext context) => context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

    private static RateLimitingOptions Current(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

    /// <summary>
    /// The caller's own bucket for the endpoint's policy, with the instance-wide limit it shares with everyone, each in
    /// the order explained where the limiter is set up: writes' shared budget after the caller's bucket, the search and
    /// verification gates before it.
    /// </summary>
    private static (RateLimitPartition<string> First, RateLimitPartition<string> Then) Limits(HttpContext context)
    {
        var options = Current(context);
        if (!options.Enabled)
            return (NoLimit, NoLimit);
        return PolicyOf(context) switch
        {
            RateLimitPolicies.SignIn => (Bucket("sign-in:" + AddressKey(context.Connection.RemoteIpAddress), options.SignIn), NoLimit),
            RateLimitPolicies.Docs => (Bucket("docs:" + AddressKey(context.Connection.RemoteIpAddress), options.Docs), NoLimit),
            RateLimitPolicies.Reads => (Bucket("reads:" + CallerKey(context), options.Reads), NoLimit),
            RateLimitPolicies.Inbox when IsSearch(context) => (Gate("searches", options.SearchesAtOnce), Bucket("search:" + CallerKey(context), options.Search)),
            RateLimitPolicies.Inbox => (Bucket("list:" + CallerKey(context), options.Reads), NoLimit),
            RateLimitPolicies.Verify => (Gate("verifications", options.VerificationsAtOnce), Bucket("verify:" + CallerKey(context), options.Verify)),
            RateLimitPolicies.Writes => (Bucket("writes:" + CallerKey(context), options.Writes), Bucket("all-writes", options.AllWrites)),
            _ => (NoLimit, NoLimit),
        };
    }

    private static RateLimitPartition<string> Bucket(string key, BucketOptions bucket) =>
        RateLimitPartition.GetTokenBucketLimiter(key, _ => TokenBucket(bucket));

    // No queue on buckets: a request over the limit is turned away at once rather than held while work piles up.
    private static TokenBucketRateLimiterOptions TokenBucket(BucketOptions bucket) => new()
    {
        TokenLimit = bucket.TokenLimit,
        TokensPerPeriod = bucket.TokensPerPeriod,
        ReplenishmentPeriod = bucket.ReplenishmentPeriod,
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static RateLimitPartition<string> Gate(string key, GateOptions gate) =>
        RateLimitPartition.GetConcurrencyLimiter(key, _ => new ConcurrencyLimiterOptions { PermitLimit = gate.PermitLimit, QueueLimit = 0 });

    /// <summary>
    /// 429 problem+json, with Retry-After in whole seconds: when the bucket has a token again, or a second for a full
    /// gate (its work takes milliseconds).
    /// </summary>
    private static async ValueTask RejectAsync(OnRejectedContext rejected, CancellationToken cancellationToken)
    {
        var context = rejected.HttpContext;
        var seconds = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)) : 1;
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Type = ProblemTypes.RateLimited,
                Title = "Too many requests.",
                Detail = $"Try again in {seconds} s.",
            },
        });
    }
}
