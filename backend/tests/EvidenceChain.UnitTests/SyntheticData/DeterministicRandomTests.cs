using EvidenceChain.SyntheticData;

namespace EvidenceChain.UnitTests.SyntheticData;

public sealed class DeterministicRandomTests
{
    [Fact]
    public void Matches_the_SplitMix64_reference_vector_for_seed_zero()
    {
        var random = new DeterministicRandom(0);
        Assert.Equal(0xE220A8397B1DCDAFUL, random.NextUInt64());
        Assert.Equal(0x6E789E6AA1B965F4UL, random.NextUInt64());
    }

    [Fact]
    public void Same_seed_gives_the_same_sequence()
    {
        var a = new DeterministicRandom(42);
        var b = new DeterministicRandom(42);
        Assert.Equal(Enumerable.Range(0, 100).Select(_ => a.Next(0, 1000)), Enumerable.Range(0, 100).Select(_ => b.Next(0, 1000)));
    }

    [Fact]
    public void Next_stays_within_bounds()
    {
        var random = new DeterministicRandom(7);
        Assert.All(Enumerable.Range(0, 10_000).Select(_ => random.Next(1024, 5121)), v => Assert.InRange(v, 1024, 5120));
    }
}
