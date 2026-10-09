namespace EvidenceChain.Api.Problems;

/// <summary>
/// RFC 9457 "type" values for the errors clients act on; they branch on these, never on titles.
/// Other statuses (401, 403, 404, 500) keep the RFC 9110 defaults.
/// </summary>
public static class ProblemTypes
{
    private const string Prefix = "urn:evidence-chain:problem:";

    /// <summary>400: a parameter, header or body field is missing or malformed; "errors" lists them.</summary>
    public const string Validation = Prefix + "validation";

    /// <summary>409: the transfer's state does not allow the command; carries the current state.</summary>
    public const string InvalidTransition = Prefix + "invalid-transition";

    /// <summary>409: If-Match named an older version; carries the current state and ETag.</summary>
    public const string StaleVersion = Prefix + "stale-version";

    /// <summary>422: the Idempotency-Key was used for a different request.</summary>
    public const string IdempotencyKeyReused = Prefix + "idempotency-key-reused";

    /// <summary>409: the first request with this Idempotency-Key has not finished.</summary>
    public const string IdempotencyInFlight = Prefix + "idempotency-in-flight";

    /// <summary>428: the write needs If-Match with the ETag last read.</summary>
    public const string PreconditionRequired = Prefix + "precondition-required";

    /// <summary>409: other writes to the same evidence kept landing first; nothing changed, so retry.</summary>
    public const string ConcurrentWrite = Prefix + "concurrent-write";

    /// <summary>409: every code for that evidence type and day is taken.</summary>
    public const string DailyIndexExhausted = Prefix + "daily-index-exhausted";
}
