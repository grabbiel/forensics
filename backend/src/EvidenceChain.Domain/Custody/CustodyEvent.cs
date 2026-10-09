namespace EvidenceChain.Domain.Custody;

/// <summary>Kinds of links in a custody chain.</summary>
public enum CustodyEventKind
{
    EvidenceRegistered,
    TransferRequested,
    TransferAccepted,
    TransferRejected,
}

/// <summary>
/// One append-only link of an evidence's custody chain. Every field the MAC covers lives on the row,
/// so a chain verifies from its own rows; the genesis event also commits the content's SHA-256.
/// </summary>
public sealed class CustodyEvent
{
    private CustodyEvent(long evidenceId, int seq, CustodyEventKind kind, DateTime occurredAtUtc, int actorId, string notes, string keyId, byte[] mac, byte canonicalVersion)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seq, 1);
        Utc.Require(occurredAtUtc, nameof(occurredAtUtc));
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        RequireMac(mac, nameof(mac));

        EvidenceId = evidenceId;
        Seq = seq;
        Kind = kind;
        OccurredAtUtc = occurredAtUtc;
        ActorId = actorId;
        Notes = notes;
        KeyId = keyId;
        _mac = mac.ToArray();
        CanonicalVersion = canonicalVersion;
    }

    // Required by EF Core for materialization.
    private CustodyEvent() { }

    /// <summary>Sequence 1: registration, committing the content hash and naming the initial custodian.</summary>
    public static CustodyEvent Genesis(
        long evidenceId, DateTime occurredAtUtc, int actorId, int initialCustodianId, byte[] contentSha256, int contentLength,
        string mediaType, string notes, string keyId, byte[] mac, byte canonicalVersion)
    {
        if (contentSha256 is not { Length: 32 })
            throw new ArgumentException("A SHA-256 is 32 bytes.", nameof(contentSha256));
        ArgumentOutOfRangeException.ThrowIfNegative(contentLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        return new CustodyEvent(evidenceId, 1, CustodyEventKind.EvidenceRegistered, occurredAtUtc, actorId, notes, keyId, mac, canonicalVersion)
        {
            ToCustodianId = initialCustodianId,
            _contentSha256 = contentSha256.ToArray(),
            ContentLength = contentLength,
            MediaType = mediaType,
        };
    }

    /// <summary>A transfer step (requested, accepted or rejected), linked to the previous event's MAC.</summary>
    public static CustodyEvent ForTransfer(
        long evidenceId, int seq, CustodyEventKind kind, DateTime occurredAtUtc, int actorId, long transferId,
        int fromCustodianId, int toCustodianId, string notes, string keyId, byte[] prevMac, byte[] mac, byte canonicalVersion)
    {
        if (kind == CustodyEventKind.EvidenceRegistered)
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Registration is always the genesis event.");
        ArgumentOutOfRangeException.ThrowIfLessThan(seq, 2);
        RequireMac(prevMac, nameof(prevMac));

        return new CustodyEvent(evidenceId, seq, kind, occurredAtUtc, actorId, notes, keyId, mac, canonicalVersion)
        {
            TransferId = transferId,
            FromCustodianId = fromCustodianId,
            ToCustodianId = toCustodianId,
            _prevMac = prevMac.ToArray(),
        };
    }

    public long CustodyEventId { get; private set; }

    public long EvidenceId { get; private set; }

    /// <summary>Position in the chain, from 1, without gaps.</summary>
    public int Seq { get; private set; }

    public CustodyEventKind Kind { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public int ActorId { get; private set; }

    public long? TransferId { get; private set; }

    public int? FromCustodianId { get; private set; }

    /// <summary>Recipient of a transfer, or the initial custodian on the genesis event.</summary>
    public int? ToCustodianId { get; private set; }

    public string Notes { get; private set; } = string.Empty;

    // Fields, so callers only ever get copies of what the MAC covers.
    private byte[]? _contentSha256;
    private byte[]? _prevMac;
    private byte[] _mac = [];

    /// <summary>Genesis only: SHA-256 of the content at registration.</summary>
    public byte[]? ContentSha256 => _contentSha256?.ToArray();

    /// <summary>Genesis only: content length at registration.</summary>
    public int? ContentLength { get; private set; }

    /// <summary>Genesis only: media type at registration.</summary>
    public string? MediaType { get; private set; }

    /// <summary>Which integrity key produced <see cref="Mac"/>, e.g. "dev" locally or "k1" in Azure.</summary>
    public string KeyId { get; private set; } = string.Empty;

    /// <summary>MAC of the previous event; null only on the genesis event.</summary>
    public byte[]? PrevMac => _prevMac?.ToArray();

    public byte[] Mac => _mac.ToArray();

    /// <summary>Version of the canonical byte encoding the MAC was computed over.</summary>
    public byte CanonicalVersion { get; private set; }

    private static void RequireMac(byte[] mac, string paramName)
    {
        if (mac is not { Length: 32 })
            throw new ArgumentException("A MAC is 32 bytes (HMAC-SHA-256).", paramName);
    }
}
