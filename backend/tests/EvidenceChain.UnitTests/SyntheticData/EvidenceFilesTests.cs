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
    private static IEnumerable<SyntheticEvidence> OfType(SyntheticDataset data, string type) => data.Evidences.Where(e => e.TypeCode == type);

    [Theory, InlineData("reference"), InlineData("scale")]
    public void Splits_evidences_40_30_30_by_type(string profile)
    {
        var data = Reference.For(profile);
        Assert.Equal(data.Profile.Evidences, data.Evidences.Count);
        Assert.All(data.Profile.TypeSplit, t => Assert.Equal(t.Count, OfType(data, t.Type).Count()));
        if (profile == "reference")
            Assert.Equal([400, 300, 300], data.Profile.TypeSplit.Select(t => t.Count));
    }

    [Theory, InlineData("reference"), InlineData("scale")]
    public void Names_and_codes_are_type_plus_utc_registration_date_plus_daily_index(string profile)
    {
        var data = Reference.For(profile);
        Assert.All(data.Evidences, e =>
        {
            Assert.Matches(FileName(), e.FileName);
            Assert.Equal($"{e.Code}.{EvidenceTypes.FileExtension(e.TypeCode)}", e.FileName);
            Assert.Equal(e.TypeCode + e.CodeDateUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + e.DailyNo.ToString("D4", CultureInfo.InvariantCulture), e.Code);
            Assert.Equal(DateOnly.FromDateTime(e.RegisteredAtUtc), e.CodeDateUtc);
            Assert.Equal(EvidenceCode.Format(e.TypeCode, e.CodeDateUtc, e.DailyNo), e.Code);
        });
        Assert.Equal(data.Evidences.Count, data.Evidences.Select(e => e.Code).Distinct().Count());
    }

    [Theory, InlineData("reference"), InlineData("scale")]
    public void Daily_indexes_count_from_one_in_registration_order_per_type_and_day(string profile)
    {
        var data = Reference.For(profile);
        foreach (var day in data.Evidences.GroupBy(e => (e.TypeCode, e.CodeDateUtc)))
        {
            var inOrder = day.OrderBy(e => e.RegisteredAtUtc).ThenBy(e => e.Code, StringComparer.Ordinal).Select(e => e.DailyNo);
            Assert.Equal(Enumerable.Range(1, day.Count()), inOrder);
        }
    }

    [Theory, InlineData("reference"), InlineData("scale")]
    public void Registrations_cover_the_90_days_before_the_anchor_with_one_busy_day(string profile)
    {
        var data = Reference.For(profile);
        Assert.All(data.Evidences, e => Assert.InRange(e.RegisteredAtUtc, data.AnchorUtc.AddDays(-90), data.AnchorUtc.AddTicks(-1)));
        Assert.True(data.Evidences.GroupBy(e => (e.TypeCode, e.CodeDateUtc)).Max(g => g.Count()) >= 40, "No day has 40 or more of one type.");
    }

    [Theory, InlineData("reference"), InlineData("scale")]
    public void Capture_precedes_registration_by_at_most_72_hours(string profile)
    {
        var data = Reference.For(profile);
        Assert.All(data.Evidences, e => Assert.InRange(e.RegisteredAtUtc - e.CapturedAtUtc, TimeSpan.Zero, TimeSpan.FromHours(72)));
    }

    [Theory, InlineData("reference"), InlineData("scale")]
    public void Sizes_stay_within_each_type_range(string profile)
    {
        var data = Reference.For(profile);
        Assert.All(OfType(data, EvidenceTypes.Log), e => Assert.InRange(e.Content.Length, 1024, 5120));
        Assert.All(OfType(data, EvidenceTypes.Csv), e => Assert.InRange(e.Content.Length, 1024, 5120));
        Assert.All(OfType(data, EvidenceTypes.Eml), e => Assert.InRange(e.Content.Length, 2048, 10240));

        var large = data.Evidences.Single(e => e.Code == data.Fixtures.LargeEmail);
        Assert.InRange(large.Content.Length, 10_000, 10_240);
    }

    [Theory, InlineData("reference"), InlineData("scale")]
    public void Files_are_utf8_without_bom_and_use_their_type_line_ending(string profile)
    {
        var data = Reference.For(profile);
        Assert.All(data.Evidences, e =>
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

    [Theory, InlineData("reference"), InlineData("scale")]
    public void Hashes_and_media_types_match_the_content(string profile)
    {
        var data = Reference.For(profile);
        Assert.All(data.Evidences, e =>
        {
            Assert.Equal(SHA256.HashData(e.Content), e.Sha256);
            Assert.Equal(DatasetBuilder.MediaType(e.TypeCode), e.MediaType);
        });
    }

    [Theory, InlineData("reference"), InlineData("scale")]
    public void Logs_are_rfc5424_lines_with_ascending_timestamps_and_documentation_addresses(string profile)
    {
        var data = Reference.For(profile);
        foreach (var log in OfType(data, EvidenceTypes.Log))
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

    [Theory, InlineData("reference"), InlineData("scale")]
    public void About_one_log_in_ten_holds_a_deny_burst_from_one_source(string profile)
    {
        var data = Reference.For(profile);
        var withBurst = OfType(data, EvidenceTypes.Log).Count(log =>
        {
            var sources = Lines(log, "\n").Select(l => DenySource().Match(l)).Select(m => m.Success ? m.Groups[1].Value : null).ToArray();
            return Enumerable.Range(0, Math.Max(0, sources.Length - 4)).Any(i => sources[i] is not null && sources.Skip(i).Take(5).All(s => s == sources[i]));
        });
        var logs = OfType(data, EvidenceTypes.Log).Count();
        Assert.InRange(withBurst, logs * 5 / 100, logs * 15 / 100); // about one in ten
    }

    [Theory, InlineData("reference"), InlineData("scale")]
    public void Csvs_follow_rfc4180_with_dot_decimals_and_test_currency(string profile)
    {
        var data = Reference.For(profile);
        foreach (var csv in OfType(data, EvidenceTypes.Csv))
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

    [Theory, InlineData("reference"), InlineData("scale")]
    public void Emails_have_ascii_headers_one_folded_header_and_a_date_whose_weekday_matches(string profile)
    {
        var data = Reference.For(profile);
        foreach (var eml in OfType(data, EvidenceTypes.Eml))
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
