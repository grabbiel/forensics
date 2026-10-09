namespace EvidenceChain.Domain.Custody;

/// <summary>Transfer lifecycle: pending until the recipient accepts or rejects.</summary>
public enum TransferStatus
{
    Pending,
    Accepted,
    Rejected,
}

/// <summary>
/// A request to hand an evidence from its current custodian to another Custodio.
/// Requests and decisions each carry an idempotency key with a fingerprint of the request body.
/// </summary>
public sealed class CustodyTransfer
{
    public CustodyTransfer(
        long evidenceId, int fromCustodianId, int toCustodianId, int requestedById, DateTime requestedAtUtc,
        string reason, Guid clientRequestId, byte[] requestFingerprint)
    {
        if (fromCustodianId == toCustodianId)
            throw new ArgumentException("A transfer needs a different recipient.", nameof(toCustodianId));
        Utc.Require(requestedAtUtc, nameof(requestedAtUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        RequireFingerprint(requestFingerprint, nameof(requestFingerprint));

        EvidenceId = evidenceId;
        FromCustodianId = fromCustodianId;
        ToCustodianId = toCustodianId;
        RequestedById = requestedById;
        RequestedAtUtc = requestedAtUtc;
        Reason = reason.Trim();
        ClientRequestId = clientRequestId;
        _requestFingerprint = requestFingerprint.ToArray();
        Status = TransferStatus.Pending;
    }

    // Required by EF Core for materialization.
    private CustodyTransfer() { }

    public long TransferId { get; private set; }

    public long EvidenceId { get; private set; }

    public int FromCustodianId { get; private set; }

    public int ToCustodianId { get; private set; }

    public int RequestedById { get; private set; }

    public DateTime RequestedAtUtc { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public TransferStatus Status { get; private set; }

    public DateTime? DecidedAtUtc { get; private set; }

    public int? DecidedById { get; private set; }

    public string? DecisionNotes { get; private set; }

    /// <summary>Idempotency-Key of the request, unique per requester.</summary>
    public Guid ClientRequestId { get; private set; }

    private byte[] _requestFingerprint = [];
    private byte[]? _decisionFingerprint;

    /// <summary>SHA-256 of the request body, so a reused key with a different body is detected.</summary>
    public byte[] RequestFingerprint => _requestFingerprint.ToArray();

    /// <summary>Idempotency-Key of the decision, unique per decider.</summary>
    public Guid? DecisionKey { get; private set; }

    public byte[]? DecisionFingerprint => _decisionFingerprint?.ToArray();

    /// <summary>Optimistic concurrency token (rowversion), exposed as the ETag.</summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Accepts the transfer; only the pending recipient may.</summary>
    public void Accept(int deciderId, DateTime decidedAtUtc, string? notes, Guid decisionKey, byte[] fingerprint) =>
        Decide(TransferStatus.Accepted, deciderId, decidedAtUtc, notes, decisionKey, fingerprint);

    /// <summary>Rejects the transfer with a reason; only the pending recipient may.</summary>
    public void Reject(int deciderId, DateTime decidedAtUtc, string reason, Guid decisionKey, byte[] fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Decide(TransferStatus.Rejected, deciderId, decidedAtUtc, reason, decisionKey, fingerprint);
    }

    private void Decide(TransferStatus outcome, int deciderId, DateTime decidedAtUtc, string? notes, Guid decisionKey, byte[] fingerprint)
    {
        if (Status != TransferStatus.Pending)
            throw new InvalidOperationException($"Transfer {TransferId} is already {Status}.");
        if (deciderId != ToCustodianId)
            throw new InvalidOperationException("Only the recipient can decide a transfer.");
        Utc.Require(decidedAtUtc, nameof(decidedAtUtc));
        if (decidedAtUtc < RequestedAtUtc)
            throw new ArgumentException("A decision cannot precede its request.", nameof(decidedAtUtc));
        RequireFingerprint(fingerprint, nameof(fingerprint));

        Status = outcome;
        DecidedById = deciderId;
        DecidedAtUtc = decidedAtUtc;
        DecisionNotes = notes?.Trim();
        DecisionKey = decisionKey;
        _decisionFingerprint = fingerprint.ToArray();
    }

    private static void RequireFingerprint(byte[] fingerprint, string paramName)
    {
        if (fingerprint is not { Length: 32 })
            throw new ArgumentException("A fingerprint is a 32-byte SHA-256.", paramName);
    }
}

/// <summary>Result of the last chain verification, as shown in the inbox.</summary>
public enum IntegrityStatus
{
    Unverified,
    Valid,
    Invalid,
}
