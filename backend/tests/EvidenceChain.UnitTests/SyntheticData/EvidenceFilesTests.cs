using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EvidenceChain.Domain.Catalog;
using EvidenceChain.SyntheticData;

namespace EvidenceChain.UnitTests.SyntheticData;

public sealed partial class EvidenceFilesTests
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static IReadOnlyList<SyntheticEvidence> Evidences => Reference.Dataset.Evidences;
    private static IEnumerable<SyntheticEvidence> OfType(string type) => Evidences.Where(e => e.TypeCode == type);

    [Fact]
    public void Has_400_logs_300_csvs_and_300_emails()
    {
        Assert.Equal(1000, Evidences.Count);
        Assert.Equal(400, OfType(EvidenceTypes.Log).Count());
        Assert.Equal(300, OfType(EvidenceTypes.Csv).Count());
        Assert.Equal(300, OfType(EvidenceTypes.Eml).Count());
    }

    [Fact]
    public void Names_and_codes_are_type_plus_utc_registration_date_plus_daily_index()
    {
        Assert.All(Evidences, e =>
        {
            Assert.Matches(FileName(), e.FileName);
            Assert.Equal($"{e.Code}.{EvidenceTypes.FileExtension(e.TypeCode)}", e.FileName);
            Assert.Equal(e.TypeCode + e.CodeDateUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + e.DailyNo.ToString("D4", CultureInfo.InvariantCulture), e.Code);
            Assert.Equal(DateOnly.FromDateTime(e.RegisteredAtUtc), e.CodeDateUtc);
            Assert.True(EvidenceCode.IsValid(e.Code));
        });
        Assert.Equal(Evidences.Count, Evidences.Select(e => e.Code).Distinct().Count());
    }

    [Fact]
    public void Daily_indexes_count_from_one_in_registration_order_per_type_and_day()
    {
        foreach (var day in Evidences.GroupBy(e => (e.TypeCode, e.CodeDateUtc)))
        {
            var inOrder = day.OrderBy(e => e.RegisteredAtUtc).ThenBy(e => e.Code, StringComparer.Ordinal).Select(e => e.DailyNo);
            Assert.Equal(Enumerable.Range(1, day.Count()), inOrder);
        }
    }

    [Fact]
    public void Registrations_cover_the_90_days_before_the_anchor_with_one_busy_day()
    {
        Assert.All(Evidences, e => Assert.InRange(e.RegisteredAtUtc, Reference.Anchor.AddDays(-90), Reference.Anchor.AddTicks(-1)));
        Assert.True(Evidences.GroupBy(e => (e.TypeCode, e.CodeDateUtc)).Max(g => g.Count()) >= 40, "No day has 40 or more of one type.");
    }

    [Fact]
    public void Capture_precedes_registration_by_at_most_72_hours()
    {
        Assert.All(Evidences, e => Assert.InRange(e.RegisteredAtUtc - e.CapturedAtUtc, TimeSpan.Zero, TimeSpan.FromHours(72)));
    }

    [Fact]
    public void Sizes_stay_within_each_type_range()
    {
        Assert.All(OfType(EvidenceTypes.Log), e => Assert.InRange(e.Content.Length, 1024, 5120));
        Assert.All(OfType(EvidenceTypes.Csv), e => Assert.InRange(e.Content.Length, 1024, 5120));
        Assert.All(OfType(EvidenceTypes.Eml), e => Assert.InRange(e.Content.Length, 2048, 10240));

        var large = Evidences.Single(e => e.Code == Reference.Dataset.Fixtures.LargeEmail);
        Assert.InRange(large.Content.Length, 10_000, 10_240);
    }

    [Fact]
    public void Files_are_utf8_without_bom_and_use_their_type_line_ending()
    {
        Assert.All(Evidences, e =>
        {
            Assert.False(e.Content.AsSpan().StartsWith((byte[])[0xEF, 0xBB, 0xBF]), $"{e.FileName} starts with a BOM.");
            StrictUtf8.GetString(e.Content); // throws on invalid UTF-8
            Assert.Equal((byte)'\n', e.Content[^1]);

            var text = Encoding.UTF8.GetString(e.Content);
            if (e.TypeCode == EvidenceTypes.Log)
                Assert.DoesNotContain('\r', text);
            else
                Assert.DoesNotContain('\n', text.Replace("\r\n", ""));
        });
    }

    [Fact]
    public void Hashes_and_media_types_match_the_content()
    {
        Assert.All(Evidences, e =>
        {
            Assert.Equal(SHA256.HashData(e.Content), e.Sha256);
            Assert.Equal(DatasetBuilder.MediaType(e.TypeCode), e.MediaType);
        });
    }

    [Fact]
    public void Logs_are_rfc5424_lines_with_ascending_timestamps_and_documentation_addresses()
    {
        foreach (var log in OfType(EvidenceTypes.Log))
        {
            var previous = DateTime.MinValue;
            foreach (var line in Lines(log, "\n"))
            {
                var match = SyslogLine().Match(line);
                Assert.True(match.Success, $"{log.FileName}: not RFC 5424: {line}");

                var at = DateTime.Parse(match.Groups["ts"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
                Assert.True(at > previous, $"{log.FileName}: timestamps must ascend.");
                previous = at;

                Assert.True(IsDocumentationAddress(match.Groups["src"].Value) && IsDocumentationAddress(match.Groups["dst"].Value), line);
            }
        }
    }

    [Fact]
    public void About_one_log_in_ten_holds_a_deny_burst_from_one_source()
    {
        var withBurst = OfType(EvidenceTypes.Log).Count(log =>
        {
            var sources = Lines(log, "\n").Select(l => DenySource().Match(l)).Select(m => m.Success ? m.Groups[1].Value : null).ToArray();
            return Enumerable.Range(0, Math.Max(0, sources.Length - 4)).Any(i => sources[i] is not null && sources.Skip(i).Take(5).All(s => s == sources[i]));
        });
        Assert.InRange(withBurst, 20, 60); // 5-15% of 400
    }

    [Fact]
    public void Csvs_follow_rfc4180_with_dot_decimals_and_test_currency()
    {
        foreach (var csv in OfType(EvidenceTypes.Csv))
        {
            var rows = Lines(csv, "\r\n").ToArray();
            Assert.Equal("TransactionId,BookingDate,ValueDate,Account,Counterparty,Description,Amount,Currency,Balance", rows[0]);

            var previousBooking = DateOnly.MinValue;
            foreach (var fields in rows.Skip(1).Select(ParseCsvRow))
            {
                Assert.Equal(9, fields.Count);
                var booking = DateOnly.ParseExact(fields[1], "yyyy-MM-dd", CultureInfo.InvariantCulture);
                Assert.True(booking >= previousBooking, $"{csv.FileName}: booking dates must not go back.");
                previousBooking = booking;
                Assert.Matches(AccountNumber(), fields[3]);
                Assert.Matches(Amount(), fields[6]);
                Assert.Equal("XTS", fields[7]);
                Assert.Matches(Amount(), fields[8]);
            }
        }
    }

    [Fact]
    public void Emails_have_ascii_headers_one_folded_header_and_a_date_whose_weekday_matches()
    {
        foreach (var eml in OfType(EvidenceTypes.Eml))
        {
            var text = StrictUtf8.GetString(eml.Content);
            var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var headers = text[..headerEnd];
            var body = text[(headerEnd + 4)..];

            Assert.True(headers.All(char.IsAscii), $"{eml.FileName}: headers must be ASCII.");
            Assert.Equal(1, headers.Split("\r\n").Count(l => l.StartsWith(' ') || l.StartsWith('\t')));
            Assert.Contains(body, c => !char.IsAscii(c)); // Spanish accents exercise UTF-8

            var date = DateHeader().Match(headers);
            Assert.True(date.Success, $"{eml.FileName}: missing or malformed Date header.");
            var day = DateTime.ParseExact(date.Groups["date"].Value, "d MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            Assert.Equal(day.DayOfWeek.ToString()[..3], date.Groups["dow"].Value);

            Assert.Matches(MessageId(), headers);
            Assert.Contains("Content-Type: text/plain; charset=utf-8", headers);
            Assert.All(EmailAddress().Matches(headers).Select(m => m.Groups[1].Value), domain =>
                Assert.True(domain is "example.com" or "example.test" || domain.EndsWith(".test", StringComparison.Ordinal), domain));
        }
    }

    private static IEnumerable<string> Lines(SyntheticEvidence file, string newline) =>
        Encoding.UTF8.GetString(file.Content).Split(newline).SkipLast(1);

    private static bool IsDocumentationAddress(string ip)
    {
        var bytes = IPAddress.Parse(ip).GetAddressBytes();
        return (bytes[0], bytes[1], bytes[2]) is (192, 0, 2) or (198, 51, 100) or (203, 0, 113);
    }

    /// <summary>Minimal RFC 4180 field splitter for one record without line breaks.</summary>
    private static List<string> ParseCsvRow(string row)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < row.Length; i++)
        {
            var c = row[i];
            if (quoted && c == '"' && i + 1 < row.Length && row[i + 1] == '"') { field.Append('"'); i++; }
            else if (c == '"') quoted = !quoted;
            else if (c == ',' && !quoted) { fields.Add(field.ToString()); field.Clear(); }
            else field.Append(c);
        }
        fields.Add(field.ToString());
        return fields;
    }

    [GeneratedRegex(@"^(LOG|CSV|EML)\d{8}\d{4}\.(log|csv|eml)$")]
    private static partial Regex FileName();

    [GeneratedRegex(@"^<13[24]>1 (?<ts>\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z) [a-z0-9-]+\.example\.test filterlog \d{4} - - action=(ALLOW|DENY) proto=(TCP|UDP) src=(?<src>[\d.]+) spt=\d+ dst=(?<dst>[\d.]+) dpt=\d+ bytes=\d+$")]
    private static partial Regex SyslogLine();

    [GeneratedRegex(@"action=DENY .*src=(203\.0\.113\.\d+)")]
    private static partial Regex DenySource();

    [GeneratedRegex(@"^XA00-\d{4}$")]
    private static partial Regex AccountNumber();

    [GeneratedRegex(@"^-?\d+\.\d{2}$")]
    private static partial Regex Amount();

    [GeneratedRegex(@"^Date: (?<dow>Mon|Tue|Wed|Thu|Fri|Sat|Sun), (?<date>\d{1,2} [A-Z][a-z]{2} \d{4} \d\d:\d\d:\d\d) \+0000\r?$", RegexOptions.Multiline)]
    private static partial Regex DateHeader();

    [GeneratedRegex(@"^Message-ID: <[0-9a-f]{16}@mail\.example\.test>\r?$", RegexOptions.Multiline)]
    private static partial Regex MessageId();

    [GeneratedRegex(@"<[^@>]+@([^>]+)>")]
    private static partial Regex EmailAddress();
}
