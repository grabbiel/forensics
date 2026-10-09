using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EvidenceChain.SyntheticData;

namespace EvidenceChain.Seeder;

/// <summary>Writes generated datasets to disk: the full export, or the committed reference files.</summary>
internal static partial class DatasetFiles
{
    private const string ManifestName = "manifest.csv";

    /// <summary>
    /// Reads --seed (default 42), --anchor (default today 00:00 UTC, printed so the run can be repeated) and the size:
    /// --profile reference|scale, optionally overridden by --evidences, --events, --max-transfers and --days.
    /// </summary>
    public static (ulong Seed, DateTime AnchorUtc, DatasetProfile Profile) ReadInputs(IReadOnlyDictionary<string, string> options)
    {
        var seed = ReferenceDataset.Seed;
        if (options.TryGetValue("seed", out var s) && !ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
            throw new ArgumentException($"--seed must be a non-negative integer, not '{s}'.");

        var anchor = DateTime.UtcNow.Date;
        if (options.TryGetValue("anchor", out var a)
            && !DateTime.TryParse(a, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out anchor))
            throw new ArgumentException($"--anchor must be an ISO 8601 instant such as 2026-10-01T00:00:00Z, not '{a}'.");

        var name = options.GetValueOrDefault("profile") ?? DatasetProfile.Reference.Name;
        var profile = DatasetProfile.Named.FirstOrDefault(p => p.Name == name)
            ?? throw new ArgumentException($"--profile must be one of: {string.Join(", ", DatasetProfile.Named.Select(p => p.Name))}.");
        var overridden = new[] { "evidences", "events", "max-transfers", "days" }.Any(options.ContainsKey);
        profile = profile with
        {
            Name = overridden ? "custom" : profile.Name,
            Evidences = Int(options, "evidences") ?? profile.Evidences,
            TotalEvents = Int(options, "events") ?? profile.TotalEvents,
            MaxTransfersPerEvidence = Int(options, "max-transfers") ?? profile.MaxTransfersPerEvidence,
            Days = Int(options, "days") ?? profile.Days,
        };

        try
        {
            profile.Validate();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new ArgumentException(ex.Message, ex);
        }

        return (seed, DateTime.SpecifyKind(anchor, DateTimeKind.Utc), profile);
    }

    private static int? Int(IReadOnlyDictionary<string, string> options, string key) =>
        !options.TryGetValue(key, out var value) ? null
        : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number
        : throw new ArgumentException($"--{key} must be a positive integer, not '{value}'.");

    /// <summary>
    /// Writes every file plus manifest.csv into <paramref name="directory"/>. A folder holding an earlier export is
    /// refused unless <paramref name="clean"/> removes it first, so the folder never disagrees with its manifest.
    /// </summary>
    public static async Task ExportAsync(SyntheticDataset dataset, string directory, bool clean, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var previous = Directory.EnumerateFiles(directory).Where(f => IsEvidenceFile(f) || Path.GetFileName(f) == ManifestName).ToArray();
        if (previous.Length > 0 && !clean)
            throw new ArgumentException($"{directory} already holds an export; pass --clean to replace it.");
        foreach (var file in previous)
            File.Delete(file);

        foreach (var evidence in dataset.Evidences)
            await File.WriteAllBytesAsync(Path.Combine(directory, evidence.FileName), evidence.Content, cancellationToken);
        await WriteManifestAsync(dataset, Path.Combine(directory, ManifestName), cancellationToken);
    }

    /// <summary>Rewrites the committed reference manifest and one sample per type under <paramref name="directory"/>.</summary>
    public static async Task WriteReferenceAsync(SyntheticDataset dataset, string directory, CancellationToken cancellationToken)
    {
        var samples = Path.Combine(directory, ReferenceDataset.SamplesFolder);
        Directory.CreateDirectory(samples);
        // Only evidence-named files are ours to replace; anything else in the folder is left alone.
        foreach (var stale in Directory.EnumerateFiles(samples).Where(IsEvidenceFile))
            File.Delete(stale);
        foreach (var evidence in ReferenceDataset.Samples(dataset))
            await File.WriteAllBytesAsync(Path.Combine(samples, evidence.FileName), evidence.Content, cancellationToken);
        await WriteManifestAsync(dataset, Path.Combine(directory, ReferenceDataset.ManifestFileName), cancellationToken);
    }

    /// <summary>Prints the counts and the fixture codes.</summary>
    public static void PrintSummary(SyntheticDataset dataset)
    {
        var f = dataset.Fixtures;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Seed {dataset.Seed}, anchor {dataset.AnchorUtc:yyyy-MM-ddTHH:mm:ssZ}, profile {dataset.Profile.Name}: {dataset.Evidences.Count} files, {dataset.Transfers.Count} transfers, {dataset.Events.Count} events."));
        Console.WriteLine($"  intact              {f.Intact}");
        Console.WriteLine($"  overdue transfer    {f.OverdueTransfer}");
        Console.WriteLine($"  fresh pending       {f.FreshPending} (to {SyntheticPeople.DemoCustodian})");
        Console.WriteLine($"  large email         {f.LargeEmail}");
        Console.WriteLine($"  event tampered      {f.EventTampered} (seq {f.EventTamperedSeq})");
        Console.WriteLine($"  content tampered    {f.ContentTampered} (byte {f.ContentTamperedOffset})");
        Console.WriteLine($"  custodian tampered  {f.CustodianTampered} (to {f.CustodianTamperedTo})");
        Console.WriteLine($"  accepted late       {string.Join(", ", f.AcceptedLate)}");
    }

    private static bool IsEvidenceFile(string path) => EvidenceFileName().IsMatch(Path.GetFileName(path));

    [GeneratedRegex(@"^(LOG|CSV|EML)\d{12}\.(log|csv|eml)$")]
    private static partial Regex EvidenceFileName();

    private static Task WriteManifestAsync(SyntheticDataset dataset, string path, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, Manifest.Render(dataset), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
}
