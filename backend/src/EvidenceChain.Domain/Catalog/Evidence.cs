namespace EvidenceChain.Domain.Catalog;

/// <summary>A registered piece of evidence: identity, timestamps, custodians and the head of its custody chain.</summary>
public sealed class Evidence
{
    /// <summary>Registers an evidence; the database composes <see cref="Code"/> from type, date and daily index.</summary>
    public Evidence(
        string typeCode,
        DateOnly codeDateUtc,
        short dailyNo,
        string description,
        DateTime capturedAtUtc,
        DateTime registeredAtUtc,
        int registeredById,
        int initialCustodianId,
        EvidenceContent content)
    {
        if (!EvidenceTypes.IsKnown(typeCode))
            throw new ArgumentOutOfRangeException(nameof(typeCode), typeCode, "Unknown evidence type.");
        ArgumentOutOfRangeException.ThrowIfLessThan(dailyNo, (short)1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dailyNo, (short)EvidenceCode.MaxDailyNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Utc.Require(capturedAtUtc, nameof(capturedAtUtc));
        Utc.Require(registeredAtUtc, nameof(registeredAtUtc));
        if (DateOnly.FromDateTime(registeredAtUtc) != codeDateUtc)
            throw new ArgumentException("The code date must be the UTC registration date.", nameof(codeDateUtc));
        if (capturedAtUtc > registeredAtUtc)
            throw new ArgumentException("Evidence cannot be captured after it is registered.", nameof(capturedAtUtc));
        ArgumentNullException.ThrowIfNull(content);

        TypeCode = typeCode;
        CodeDateUtc = codeDateUtc;
        DailyNo = dailyNo;
        Description = description.Trim();
        CapturedAtUtc = capturedAtUtc;
        RegisteredAtUtc = registeredAtUtc;
        RegisteredById = registeredById;
        InitialCustodianId = initialCustodianId;
        CurrentCustodianId = initialCustodianId;
        Content = content;
    }

    // Required by EF Core for materialization.
    private Evidence() { }

    public long EvidenceId { get; private set; }

    public string TypeCode { get; private set; } = string.Empty;

    /// <summary>UTC registration date; part of the code.</summary>
    public DateOnly CodeDateUtc { get; private set; }

    /// <summary>Index within (type, date), 1..9999.</summary>
    public short DailyNo { get; private set; }

    /// <summary>Persisted computed column: TYPE + YYYYMMDD + INDEX.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public DateTime CapturedAtUtc { get; private set; }

    public DateTime RegisteredAtUtc { get; private set; }

    public int RegisteredById { get; private set; }

    /// <summary>Who held the evidence at registration; a database trigger keeps it immutable.</summary>
    public int InitialCustodianId { get; private set; }

    public int CurrentCustodianId { get; private set; }

    /// <summary>MAC of the newest custody event; null until the genesis event is appended.</summary>
    private byte[]? _headMac;

    public byte[]? HeadMac => _headMac?.ToArray();

    public int EventCount { get; private set; }

    /// <summary>True for the named fixtures the README points at.</summary>
    public bool IsDemoFixture { get; private set; }

    /// <summary>Optimistic concurrency token (rowversion).</summary>
    public byte[] RowVersion { get; private set; } = [];

    public EvidenceContent Content { get; private set; } = null!;

    /// <summary>Makes the newly appended event the chain head; sequences must be contiguous.</summary>
    public void AdvanceHead(int seq, byte[] mac)
    {
        if (seq != EventCount + 1)
            throw new InvalidOperationException($"Event {seq} does not follow the head {EventCount}.");
        if (mac is not { Length: 32 })
            throw new ArgumentException("A MAC is 32 bytes (HMAC-SHA-256).", nameof(mac));

        _headMac = mac.ToArray();
        EventCount = seq;
    }

    /// <summary>Moves custody to the recipient of an accepted transfer.</summary>
    public void HandOverTo(int custodianId) => CurrentCustodianId = custodianId;
}
