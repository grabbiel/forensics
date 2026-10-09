namespace EvidenceChain.Application.Idempotency;

/// <summary>The Idempotency-Key was already used for a different request.</summary>
public sealed class IdempotencyKeyReusedException(Guid key)
    : Exception($"Idempotency-Key {key} was already used for a different request.")
{
    public Guid Key { get; } = key;
}

/// <summary>A request with the same Idempotency-Key is still being processed.</summary>
public sealed class IdempotencyInFlightException(Guid key)
    : Exception($"A request with Idempotency-Key {key} is still being processed; retry shortly.")
{
    public Guid Key { get; } = key;
}
