using System.Globalization;
using System.Text;

namespace EvidenceChain.SyntheticData.Content;

/// <summary>Accumulates UTF-8 lines (no BOM) with one fixed newline, capping each line's size.</summary>
internal sealed class FileBuilder(string newline, int maxLineBytes)
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly MemoryStream _bytes = new();

    /// <summary>Bytes written so far.</summary>
    public int Length => (int)_bytes.Length;

    /// <summary>Appends one complete line; throws if it would break the size guarantee.</summary>
    public void Line(string text)
    {
        var bytes = Utf8.GetBytes(text + newline);
        if (bytes.Length > maxLineBytes)
            throw new InvalidOperationException($"Line of {bytes.Length} bytes exceeds the {maxLineBytes}-byte cap: {text}");
        _bytes.Write(bytes);
    }

    public byte[] ToArray() => _bytes.ToArray();

    /// <summary>
    /// Target for "fill until reached, end on a complete line": drawn so the overshoot (under one line) stays in range.
    /// </summary>
    public static int Target(DeterministicRandom random, int minBytes, int maxBytes, int maxLineBytes) =>
        random.Next(minBytes, maxBytes - maxLineBytes + 1);
}

/// <summary>Culture-invariant formatting shared by the generators.</summary>
internal static class Invariant
{
    private static readonly string[] SpanishMonths =
        ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    /// <summary>ISO 8601 UTC with milliseconds, e.g. 2026-09-14T14:03:22.418Z.</summary>
    public static string Iso(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>yyyy-MM-dd.</summary>
    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>RFC 5322 date in UTC, e.g. Mon, 14 Sep 2026 14:03:22 +0000.</summary>
    public static string Rfc5322(DateTime utc) => utc.ToString("ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " +0000";

    /// <summary>Spanish month name and year, e.g. "septiembre 2026", independent of the machine's culture.</summary>
    public static string SpanishMonthYear(DateOnly date) => $"{SpanishMonths[date.Month - 1]} {date.Year.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Two-decimal amount with a dot, e.g. -1234.50.</summary>
    public static string Money(long cents) => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Interpolation without the current culture.</summary>
    public static string F(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
