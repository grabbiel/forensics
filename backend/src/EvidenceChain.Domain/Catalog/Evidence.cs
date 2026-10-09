namespace EvidenceChain.Domain.Catalog;

/// <summary>A registered piece of evidence. Tracer shape; custodians, content and chain head arrive on Day 2.</summary>
public sealed class Evidence
{
    /// <summary>Creates an evidence with its code parts; the database composes <see cref="Code"/>.</summary>
    public Evidence(string typeCode, DateOnly codeDateUtc, short dailyNo, string description)
    {
        if (!EvidenceTypes.IsKnown(typeCode))
            throw new ArgumentOutOfRangeException(nameof(typeCode), typeCode, "Unknown evidence type.");
        ArgumentOutOfRangeException.ThrowIfLessThan(dailyNo, (short)1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dailyNo, (short)EvidenceCode.MaxDailyNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        TypeCode = typeCode;
        CodeDateUtc = codeDateUtc;
        DailyNo = dailyNo;
        Description = description.Trim();
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
}
