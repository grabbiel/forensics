using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.People;

namespace EvidenceChain.Domain.Custody;

/// <summary>Transfer lifecycle: pending until the recipient accepts or rejects.</summary>
public enum TransferStatus
{
    Pending,
    Accepted,
    Rejected,
}

/// <summary>
/// A request to hand an evidence from its current custodian to another Custodio. Every change goes through
/// <see cref="TransferTransitions"/>; requests and decisions each carry an idempotency key and a body fingerprint.
/// </summary>
public sealed class CustodyTransfer
{
    private CustodyTransfer(
        long evidenceId, int fromCustodianId, int toCustodianId, int requestedById, DateTime requestedAtUtc,
        string reason, Guid clientRequestId, byte[] requestFingerprint)
    {
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

    /// <summary>
    /// An Investigador asks the evidence's current custodian to hand it to <paramref name="recipient"/>, a different Custodio.
    /// <paramref name="hasPendingTransfer"/> refuses a second open request (the database enforces it too).
    /// </summary>
    public static CustodyTransfer Request(
        Evidence evidence, Actor requester, Actor recipient, bool hasPendingTransfer, DateTime requestedAtUtc,
        string reason, Guid clientRequestId, byte[] requestFingerprint)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var decision = TransferTransitions.Decide(hasPendingTransfer ? TransferStatus.Pending : null, TransferCommand.Request, requester.Role, isRecipient: false);
        TransferRuleException.ThrowIfRefused(decision, TransferCommand.Request, requester.Role, transfer: null);

        if (recipient.Role != UserRole.Custodio)
            throw new ArgumentException("The recipient must be a Custodio.", nameof(recipient));
        if (recipient.UserId == evidence.CurrentCustodianId)
            throw new ArgumentException("The recipient already holds the evidence.", nameof(recipient));
        if (recipient.UserId == requester.UserId)
            throw new ArgumentException("A requester cannot send the evidence to themselves.", nameof(recipient));
        Utc.Require(requestedAtUtc, nameof(requestedAtUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        RequireFingerprint(requestFingerprint, nameof(requestFingerprint));

        return new CustodyTransfer(evidence.EvidenceId, evidence.CurrentCustodianId, recipient.UserId, requester.UserId,
            requestedAtUtc, reason, clientRequestId, requestFingerprint);
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

    /// <summary>The recipient accepts: custody passes to them on <paramref name="evidence"/> in the same step.</summary>
    public void Accept(Evidence evidence, Actor decider, DateTime decidedAtUtc, string? notes, Guid decisionKey, byte[] fingerprint)
    {
        Decide(TransferCommand.Accept, evidence, decider, decidedAtUtc, notes, decisionKey, fingerprint);
        evidence.HandOverTo(ToCustodianId);
    }

    /// <summary>The recipient rejects with a reason; custody stays where it was.</summary>
    public void Reject(Evidence evidence, Actor decider, DateTime decidedAtUtc, string reason, Guid decisionKey, byte[] fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Decide(TransferCommand.Reject, evidence, decider, decidedAtUtc, reason, decisionKey, fingerprint);
    }

    private void Decide(TransferCommand command, Evidence evidence, Actor decider, DateTime decidedAtUtc, string? notes, Guid decisionKey, byte[] fingerprint)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.EvidenceId != EvidenceId)
            throw new ArgumentException($"Transfer {TransferId} belongs to another evidence.", nameof(evidence));

        var decision = TransferTransitions.Decide(Status, command, decider.Role, isRecipient: decider.UserId == ToCustodianId);
        TransferRuleException.ThrowIfRefused(decision, command, decider.Role, this);

        // Custody only moves through accepted transfers, so it must still be where the request found it.
        if (evidence.CurrentCustodianId != FromCustodianId)
            throw new InvalidOperationException($"Evidence {evidence.Code} changed custodian after transfer {TransferId} was requested.");
        Utc.Require(decidedAtUtc, nameof(decidedAtUtc));
        if (decidedAtUtc < RequestedAtUtc)
            throw new ArgumentException("A decision cannot precede its request.", nameof(decidedAtUtc));
        RequireFingerprint(fingerprint, nameof(fingerprint));

        Status = decision.Next!.Value;
        DecidedById = decider.UserId;
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
