namespace EvidenceChain.Domain.Catalog;

/// <summary>Last daily index handed out per (type, UTC day); allocation happens in SQL under a range lock.</summary>
public sealed class DailyCounter
{
    public DailyCounter(string typeCode, DateOnly day, short lastNo)
    {
        if (!EvidenceTypes.IsKnown(typeCode))
            throw new ArgumentOutOfRangeException(nameof(typeCode), typeCode, "Unknown evidence type.");
        ArgumentOutOfRangeException.ThrowIfLessThan(lastNo, (short)1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lastNo, (short)EvidenceCode.MaxDailyNo);

        TypeCode = typeCode;
        Day = day;
        LastNo = lastNo;
    }

    // Required by EF Core for materialization.
    private DailyCounter() { }

    public string TypeCode { get; private set; } = string.Empty;

    public DateOnly Day { get; private set; }

    public short LastNo { get; private set; }
}
