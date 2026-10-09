using System.Text;
using EvidenceChain.SyntheticData;

namespace EvidenceChain.UnitTests.SyntheticData;

public sealed class ReferenceDatasetTests
{
    // When these fail after an intended generator change, refresh the files with:
    //   dotnet run --project backend/tools/EvidenceChain.Seeder -- reference
    [Fact]
    public void Regenerating_seed_42_reproduces_the_committed_manifest()
    {
        // Bytes, not text: a BOM or a line-ending change must fail too.
        var committed = File.ReadAllBytes(Reference.RepoFile("database", "synthetic", ReferenceDataset.ManifestFileName));
        Assert.Equal(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Manifest.Render(Reference.Dataset)), committed);
    }

    [Fact]
    public void Committed_samples_are_byte_identical()
    {
        var folder = Reference.RepoFile("database", "synthetic", ReferenceDataset.SamplesFolder);
        var samples = ReferenceDataset.Samples(Reference.Dataset);

        Assert.Equal(samples.Select(s => s.FileName).Order(StringComparer.Ordinal), Directory.EnumerateFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.All(samples, s => Assert.Equal(s.Content, File.ReadAllBytes(Path.Combine(folder, s.FileName))));
    }

    [Fact]
    public void Same_inputs_give_the_same_bytes_and_another_seed_does_not()
    {
        Assert.Equal(Manifest.Render(Reference.Dataset), Manifest.Render(DatasetBuilder.Build(ReferenceDataset.Seed, ReferenceDataset.AnchorUtc)));
        Assert.NotEqual(Manifest.Render(Reference.Dataset), Manifest.Render(DatasetBuilder.Build(43, ReferenceDataset.AnchorUtc)));
    }

    [Fact]
    public void An_anchor_with_a_time_of_day_keeps_every_timestamp_in_its_window()
    {
        var anchor = new DateTime(2026, 10, 1, 13, 37, 0, DateTimeKind.Utc);
        var dataset = DatasetBuilder.Build(7, anchor);

        Assert.All(dataset.Evidences, e => Assert.InRange(e.RegisteredAtUtc, anchor.AddDays(-90), anchor.AddTicks(-1)));
        Assert.All(dataset.Events, e => Assert.True(e.OccurredAtUtc < anchor));
        Assert.True(dataset.Evidences.GroupBy(e => (e.TypeCode, e.CodeDateUtc)).Max(g => g.Count()) >= 40);
        Assert.Equal(10_000, dataset.Events.Count);
    }

    [Fact]
    public void A_non_utc_anchor_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => DatasetBuilder.Build(42, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Local)));
    }
}
