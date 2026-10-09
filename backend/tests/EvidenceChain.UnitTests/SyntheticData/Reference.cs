using System.Collections.Concurrent;
using EvidenceChain.SyntheticData;

namespace EvidenceChain.UnitTests.SyntheticData;

/// <summary>Datasets for seed 42 and the reference anchor, built once per profile and shared by every test.</summary>
internal static class Reference
{
    private static readonly ConcurrentDictionary<string, Lazy<SyntheticDataset>> Built = new();

    public static SyntheticDataset Dataset => For(DatasetProfile.Reference.Name);

    /// <summary>The dataset for a named profile ("reference" or "scale").</summary>
    public static SyntheticDataset For(string profile) =>
        Built.GetOrAdd(profile, name => new Lazy<SyntheticDataset>(() =>
            DatasetBuilder.Build(ReferenceDataset.Seed, ReferenceDataset.AnchorUtc, DatasetProfile.Named.Single(p => p.Name == name)))).Value;

    /// <summary>Walks up from the test output folder to a file under the repository root.</summary>
    public static string RepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "backend")) && Directory.Exists(Path.Combine(dir.FullName, "database")))
                return Path.Combine([dir.FullName, .. parts]);
        }
        throw new DirectoryNotFoundException($"Repository root not found above {AppContext.BaseDirectory}.");
    }
}
