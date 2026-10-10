namespace EvidenceChain.Api.RateLimiting;

/// <summary>
/// Bound from "RateLimiting". The defaults leave the live demo's real use well clear of every limit: only loops, floods
/// and scripts reach them. An App Service setting overrides any of them (RateLimiting__Search__TokenLimit, …).
/// </summary>
public sealed class RateLimitingOptions
{
    public const string Section = "RateLimiting";

    /// <summary>Off, every request passes; the policies stay attached.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Anonymous sign-ins per client address. A demo signs in about 4 times; the rest is room for typos and a shared NAT.</summary>
    public BucketOptions SignIn { get; set; } = new() { TokenLimit = 10, TokensPerPeriod = 1, ReplenishmentPeriod = TimeSpan.FromSeconds(3) };

    /// <summary>The OpenAPI document per client address: it is generated on every request.</summary>
    public BucketOptions Docs { get; set; } = new() { TokenLimit = 10, TokensPerPeriod = 1, ReplenishmentPeriod = TimeSpan.FromSeconds(2) };

    /// <summary>Reads per user (the inbox list in a bucket of its own). A detail page is two requests; only a loop gets near.</summary>
    public BucketOptions Reads { get; set; } = new() { TokenLimit = 60, TokensPerPeriod = 2, ReplenishmentPeriod = TimeSpan.FromSeconds(1) };

    /// <summary>Inbox text searches per user: each scans the inbox, and typing sends 2 to 5 after the SPA's debounce.</summary>
    public BucketOptions Search { get; set; } = new() { TokenLimit = 15, TokensPerPeriod = 1, ReplenishmentPeriod = TimeSpan.FromSeconds(2) };

    /// <summary>Chain verifications per user: each recomputes every MAC and records the verdict.</summary>
    public BucketOptions Verify { get; set; } = new() { TokenLimit = 3, TokensPerPeriod = 1, ReplenishmentPeriod = TimeSpan.FromSeconds(10) };

    /// <summary>Custody writes per user, replays included. The two-tab conflict demo is two writes and a retry.</summary>
    public BucketOptions Writes { get; set; } = new() { TokenLimit = 10, TokensPerPeriod = 1, ReplenishmentPeriod = TimeSpan.FromSeconds(3) };

    /// <summary>Custody writes by everyone together: signing in takes only a name, so anyone can write as all 11 users.</summary>
    public BucketOptions AllWrites { get; set; } = new() { TokenLimit = 30, TokensPerPeriod = 1, ReplenishmentPeriod = TimeSpan.FromSeconds(1) };

    /// <summary>Text searches running at once on this instance; one more is turned away for a second, the work takes milliseconds.</summary>
    public GateOptions SearchesAtOnce { get; set; } = new() { PermitLimit = 4 };

    /// <summary>Chain verifications running at once on this instance.</summary>
    public GateOptions VerificationsAtOnce { get; set; } = new() { PermitLimit = 2 };

    internal IEnumerable<BucketOptions> Buckets => [SignIn, Docs, Reads, Search, Verify, Writes, AllWrites];

    internal IEnumerable<GateOptions> Gates => [SearchesAtOnce, VerificationsAtOnce];

    internal static bool IsValid(RateLimitingOptions options) =>
        options.Buckets.All(b => b.TokenLimit > 0 && b.TokensPerPeriod > 0 && b.ReplenishmentPeriod > TimeSpan.Zero)
        && options.Gates.All(g => g.PermitLimit > 0);
}

/// <summary>A token bucket: TokenLimit requests at once, then TokensPerPeriod more every ReplenishmentPeriod.</summary>
public sealed class BucketOptions
{
    public int TokenLimit { get; set; }

    public int TokensPerPeriod { get; set; }

    public TimeSpan ReplenishmentPeriod { get; set; }
}

/// <summary>At most PermitLimit requests running at once; no line, so a request is never held while it waits.</summary>
public sealed class GateOptions
{
    public int PermitLimit { get; set; }
}
