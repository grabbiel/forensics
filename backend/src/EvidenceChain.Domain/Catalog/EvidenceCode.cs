using System.Globalization;

namespace EvidenceChain.Domain.Catalog;

/// <summary>Builds evidence codes: TYPE + YYYYMMDD + INDEX (four digits).</summary>
public static class EvidenceCode
{
    /// <summary>Highest daily index a code can hold.</summary>
    public const int MaxDailyNo = 9999;

    /// <summary>Formats a code; rejects indexes outside 1..9999 because "D4" is only a minimum width.</summary>
    public static string Format(string typeCode, DateOnly codeDateUtc, int dailyNo)
    {
        if (!EvidenceTypes.IsKnown(typeCode))
            throw new ArgumentOutOfRangeException(nameof(typeCode), typeCode, "Unknown evidence type.");
        ArgumentOutOfRangeException.ThrowIfLessThan(dailyNo, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dailyNo, MaxDailyNo);

        return string.Concat(
            typeCode,
            codeDateUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            dailyNo.ToString("D4", CultureInfo.InvariantCulture));
    }
}
