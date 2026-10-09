using System.Globalization;
using System.Text;
using EvidenceChain.Domain.Catalog;
using EvidenceChain.SyntheticData.Content;

namespace EvidenceChain.SyntheticData;

/// <summary>The dataset's index as CSV (LF, UTF-8), one row per evidence in code order.</summary>
public static class Manifest
{
    public const string Header =
        "code,type,code_date,daily_no,file_name,bytes,sha256,media_type,captured_at_utc,registered_at_utc,registered_by,initial_custodian,current_custodian,event_count,fixture";

    /// <summary>Renders the manifest; identical input gives identical bytes.</summary>
    public static string Render(SyntheticDataset dataset)
    {
        var text = new StringBuilder(Header).Append('\n');
        foreach (var e in dataset.Evidences.OrderBy(e => e.Code, StringComparer.Ordinal))
        {
            string[] fields =
            [
                e.Code, e.TypeCode, Invariant.Date(e.CodeDateUtc), e.DailyNo.ToString(CultureInfo.InvariantCulture), e.FileName,
                e.Content.Length.ToString(CultureInfo.InvariantCulture), Convert.ToHexStringLower(e.Sha256), e.MediaType,
                Invariant.Iso(e.CapturedAtUtc), Invariant.Iso(e.RegisteredAtUtc), e.RegisteredBy, e.InitialCustodian,
                e.CurrentCustodian, e.EventCount.ToString(CultureInfo.InvariantCulture), e.Fixture ?? "",
            ];
            text.AppendJoin(',', fields.Select(CsvFileGenerator.Quote)).Append('\n');
        }
        return text.ToString();
    }
}

/// <summary>The committed reference: database/synthetic/manifest.reference.csv and samples/ come from these inputs.</summary>
public static class ReferenceDataset
{
    public const ulong Seed = 42;
    public static readonly DateTime AnchorUtc = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    public const string ManifestFileName = "manifest.reference.csv";
    public const string SamplesFolder = "samples";

    /// <summary>One sample file per type: the earliest code of each, so the choice never depends on content.</summary>
    public static IReadOnlyList<SyntheticEvidence> Samples(SyntheticDataset dataset) =>
        EvidenceTypes.All
            .Select(type => dataset.Evidences.Where(e => e.TypeCode == type).MinBy(e => e.Code, StringComparer.Ordinal)!)
            .ToArray();
}
