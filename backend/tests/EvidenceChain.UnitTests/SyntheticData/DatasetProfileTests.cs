using EvidenceChain.SyntheticData;

namespace EvidenceChain.UnitTests.SyntheticData;

public sealed class DatasetProfileTests
{
    [Fact]
    public void Named_profiles_are_valid_and_scale_has_200000_events()
    {
        Assert.All(DatasetProfile.Named, p => p.Validate());
        Assert.Equal((20_000, 200_000), (DatasetProfile.Scale.Evidences, DatasetProfile.Scale.TotalEvents));
        Assert.Equal([8_000, 6_000, 6_000], DatasetProfile.Scale.TypeSplit.Select(t => t.Count));
    }

    [Fact]
    public void Ordinary_pending_scales_with_evidences_and_keeps_the_event_total_reachable()
    {
        Assert.Equal(2, DatasetProfile.Reference.OrdinaryPending);
        Assert.Equal(40, DatasetProfile.Scale.OrdinaryPending);
        Assert.All(DatasetProfile.Named, p => Assert.Equal(0, (p.TotalEvents - p.Evidences - p.OrdinaryPending - 2) % 2));
    }

    [Fact]
    public void Sizes_beyond_what_the_rules_allow_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => (DatasetProfile.Reference with { TotalEvents = 30_000 }).Validate()); // > 1,000 x 21
        Assert.Throws<ArgumentOutOfRangeException>(() => (DatasetProfile.Reference with { TotalEvents = 900 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (DatasetProfile.Reference with { Evidences = 400_000 }).Validate());
        // Within the arithmetic cap (1,000 x 201) but not the calendar: recent evidences and pending slots leave too little room.
        Assert.Throws<ArgumentException>(() =>
            DatasetBuilder.Build(42, ReferenceDataset.AnchorUtc, DatasetProfile.Reference with { MaxTransfersPerEvidence = 100, TotalEvents = 201_000 }));
    }

    [Fact]
    public void Custom_sizes_allow_long_chains_under_the_same_rules()
    {
        var profile = DatasetProfile.Reference with { Name = "long-chains", MaxTransfersPerEvidence = 40, TotalEvents = 50_000 };
        var data = DatasetBuilder.Build(42, ReferenceDataset.AnchorUtc, profile);

        Assert.Equal(50_000, data.Events.Count);
        Assert.Contains(data.Evidences, e => e.EventCount > 21); // longer than the reference cap allows
        Assert.All(data.Transfers.GroupBy(t => t.EvidenceCode), g => Assert.True(g.Count() <= 40));
        Assert.Equal(3, data.Transfers.Count(t => t.DecidedAtUtc - t.RequestedAtUtc > TimeSpan.FromHours(48)));
    }
}
