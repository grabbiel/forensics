using System.Security.Cryptography;
using EvidenceChain.Domain.Catalog;
using EvidenceChain.SyntheticData.Content;

namespace EvidenceChain.SyntheticData;

/// <summary>
/// Builds the whole synthetic dataset in memory. Pure: same seed and anchor, same bytes; no clock, GUIDs or culture.
/// </summary>
public static class DatasetBuilder
{
    /// <summary>The reference size: 1,000 evidence files and exactly 10,000 custody events.</summary>
    public static SyntheticDataset Build(ulong seed, DateTime anchorUtc) => Build(seed, anchorUtc, DatasetProfile.Reference);

    /// <summary>Generates users, the profile's evidence files, their transfers and exactly its event total.</summary>
    public static SyntheticDataset Build(ulong seed, DateTime anchorUtc, DatasetProfile profile)
    {
        if (anchorUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The anchor must be a UTC instant.", nameof(anchorUtc));
        profile.Validate();

        // One forked stream per phase, so changing one phase never shifts another's draws.
        var random = new DeterministicRandom(seed);
        var planned = EvidenceSchedule.Plan(random.Fork(), anchorUtc, profile);
        var fixtures = FixturePlan.Choose(planned, anchorUtc, profile.OrdinaryPending, random.Fork());
        var custody = CustodySimulator.Simulate(planned, fixtures, profile, anchorUtc, random.Fork());
        var fixtureNames = fixtures.NamesByCode();

        var evidences = planned.Select(p =>
        {
            var (content, description) = p.TypeCode switch
            {
                EvidenceTypes.Log => LogFileGenerator.Generate(p.ContentRandom, p.CapturedAtUtc),
                EvidenceTypes.Csv => CsvFileGenerator.Generate(p.ContentRandom, p.CapturedAtUtc),
                _ => EmlFileGenerator.Generate(p.ContentRandom, p.CapturedAtUtc, large: p == fixtures.LargeEmail),
            };
            return new SyntheticEvidence(
                p.Code, p.TypeCode, DateOnly.FromDateTime(p.RegisteredAtUtc), p.DailyNo,
                $"{p.Code}.{EvidenceTypes.FileExtension(p.TypeCode)}", MediaType(p.TypeCode),
                content, SHA256.HashData(content), description, p.CapturedAtUtc, p.RegisteredAtUtc, p.RegisteredBy,
                p.InitialCustodian, custody.CurrentCustodian[p.Code], custody.EventCount[p.Code], fixtureNames.GetValueOrDefault(p.Code));
        }).ToArray();

        var tamperRandom = random.Fork();
        var contentTampered = evidences.Single(e => e.Code == fixtures.ContentTampered.Code);
        var custodianTampered = evidences.Single(e => e.Code == fixtures.CustodianTampered.Code);
        var otherCustodians = SyntheticPeople.WithRole(SyntheticRole.Custodio).Where(c => c != custodianTampered.CurrentCustodian).ToArray();

        var publicFixtures = new DatasetFixtures(
            fixtures.Intact.Code,
            fixtures.OverdueTransfer.Code,
            fixtures.FreshPending.Code,
            fixtures.LargeEmail.Code,
            fixtures.EventTampered.Code,
            EventTamperedSeq: 3,
            contentTampered.Code,
            ContentTamperedOffset: AsciiLetterOffset(contentTampered.Content),
            custodianTampered.Code,
            CustodianTamperedTo: tamperRandom.Pick(otherCustodians),
            fixtures.AcceptedLate.Select(e => e.Code).Order(StringComparer.Ordinal).ToArray());

        return new SyntheticDataset(seed, anchorUtc, profile, SyntheticPeople.All, evidences, custody.Transfers, custody.Events, publicFixtures);
    }

    /// <summary>Media type stored with each file.</summary>
    public static string MediaType(string typeCode) => typeCode switch
    {
        EvidenceTypes.Log => "text/plain; charset=utf-8",
        EvidenceTypes.Csv => "text/csv; charset=utf-8",
        EvidenceTypes.Eml => "message/rfc822",
        _ => throw new ArgumentOutOfRangeException(nameof(typeCode), typeCode, "Unknown evidence type."),
    };

    /// <summary>First ASCII letter at or after the middle; flipping its case keeps the file valid UTF-8.</summary>
    private static int AsciiLetterOffset(byte[] content)
    {
        for (var i = content.Length / 2; i < content.Length; i++)
        {
            if (char.IsAsciiLetter((char)content[i]))
                return i;
        }
        throw new InvalidOperationException("No ASCII letter to tamper with.");
    }
}
