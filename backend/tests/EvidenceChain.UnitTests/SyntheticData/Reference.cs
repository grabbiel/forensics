using EvidenceChain.SyntheticData;

namespace EvidenceChain.UnitTests.SyntheticData;

/// <summary>The reference dataset, built once and shared by every synthetic-data test.</summary>
internal static class Reference
{
    private static readonly Lazy<SyntheticDataset> Lazy = new(() => DatasetBuilder.Build(ReferenceDataset.Seed, ReferenceDataset.AnchorUtc));

    public static SyntheticDataset Dataset => Lazy.Value;

    public static DateTime Anchor => ReferenceDataset.AnchorUtc;

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
