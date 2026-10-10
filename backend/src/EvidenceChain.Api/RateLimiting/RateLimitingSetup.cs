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
}

/// <summary>
/// Token buckets per user (per client address before sign-in) on each endpoint, plus instance-wide gates on the work
/// that costs the database most. All state is in memory: with more than one instance, each counts on its own.
/// A rejection is a 429 problem with Retry-After, sent before the endpoint runs, so a throttled write saved nothing.
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
            limiter.AddPolicy(RateLimitPolicies.SignIn, context => Bucket(context, "", AddressKey(context.Connection.RemoteIpAddress), o => o.SignIn));
            limiter.AddPolicy(RateLimitPolicies.Docs, context => Bucket(context, "", AddressKey(context.Connection.RemoteIpAddress), o => o.Docs));
            limiter.AddPolicy(RateLimitPolicies.Reads, context => Bucket(context, "", UserKey(context), o => o.Reads));
            // One policy keeps its own partitions, so the inbox's two buckets live here rather than borrowing Reads'.
            limiter.AddPolicy(RateLimitPolicies.Inbox, context => IsSearch(context)
                ? Bucket(context, "search:", UserKey(context), o => o.Search)
                : Bucket(context, "list:", UserKey(context), o => o.Reads));
            limiter.AddPolicy(RateLimitPolicies.Verify, context => Bucket(context, "", UserKey(context), o => o.Verify));
            limiter.AddPolicy(RateLimitPolicies.Writes, context => Bucket(context, "", UserKey(context), o => o.Writes));
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(InstanceGate);
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

    /// <summary>The validated token's user; authorization has already turned away requests without one.</summary>
    private static string UserKey(HttpContext context) =>
        context.User.FindFirst(TokenClaims.UserId)?.Value is { } id ? $"user:{id}" : AddressKey(context.Connection.RemoteIpAddress);

    private static bool IsSearch(HttpContext context) => !string.IsNullOrWhiteSpace(context.Request.Query["q"]);

    private static RateLimitingOptions Current(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

    private static RateLimitPartition<string> Bucket(HttpContext context, string kind, string key, Func<RateLimitingOptions, BucketOptions> choose)
    {
        var options = Current(context);
        return options.Enabled ? RateLimitPartition.GetTokenBucketLimiter(kind + key, _ => TokenBucket(choose(options))) : NoLimit;
    }

    /// <summary>Writes by everyone together, and how many searches or verifications run at once; nothing else.</summary>
    private static RateLimitPartition<string> InstanceGate(HttpContext context)
    {
        var options = Current(context);
        if (!options.Enabled)
            return NoLimit;
        return context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName switch
        {
            RateLimitPolicies.Writes => RateLimitPartition.GetTokenBucketLimiter("all-writes", _ => TokenBucket(options.AllWrites)),
            RateLimitPolicies.Verify => RateLimitPartition.GetConcurrencyLimiter("verifications", _ => Gate(options.VerificationsAtOnce)),
            RateLimitPolicies.Inbox when IsSearch(context) => RateLimitPartition.GetConcurrencyLimiter("searches", _ => Gate(options.SearchesAtOnce)),
            _ => NoLimit,
        };
    }

    // No queue on buckets: a request over the limit is turned away at once rather than held while work piles up.
    private static TokenBucketRateLimiterOptions TokenBucket(BucketOptions bucket) => new()
    {
        TokenLimit = bucket.TokenLimit,
        TokensPerPeriod = bucket.TokensPerPeriod,
        ReplenishmentPeriod = bucket.ReplenishmentPeriod,
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static ConcurrencyLimiterOptions Gate(GateOptions gate) => new()
    {
        PermitLimit = gate.PermitLimit,
        QueueLimit = gate.QueueLimit,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    };

    /// <summary>
    /// 429 problem+json, with Retry-After in whole seconds: when the bucket has a token again, or a second for a gate
    /// whose line was full (its work takes milliseconds).
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
