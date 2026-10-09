using System.Globalization;
using System.Text.RegularExpressions;

namespace EvidenceChain.Domain.Catalog;

/// <summary>Builds and checks evidence codes: TYPE + YYYYMMDD + INDEX (four digits).</summary>
public static partial class EvidenceCode
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

    /// <summary>True when the code has the right shape and a real calendar date.</summary>
    public static bool IsValid(string? code) =>
        code is not null
        && Shape().IsMatch(code)
        && DateOnly.TryParseExact(code.AsSpan(3, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
        && code[^4..] != "0000";

    [GeneratedRegex(@"^(LOG|CSV|EML)\d{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
