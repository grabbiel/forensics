using EvidenceChain.Api.Http;

namespace EvidenceChain.IntegrationTests;

public sealed class EntityTagsTests
{
    [Fact]
    public void A_version_round_trips_as_quoted_lowercase_hex()
    {
        byte[] version = [0, 0, 0, 0, 0, 0, 0x07, 0xD1];

        Assert.Equal("\"00000000000007d1\"", EntityTags.Format(version));
        Assert.True(EntityTags.TryParse(" \"00000000000007D1\" ", out var parsed));
        Assert.Equal(version, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("W/\"00000000000007d1\"")] // weak tags never match a version
    [InlineData("00000000000007d1")]
    [InlineData("\"00000000000007d1\", \"00000000000007d2\"")]
    [InlineData("\"000000000007d1\"")]
    [InlineData("\"00000000000007zz\"")]
    public void Anything_but_one_strong_tag_of_this_api_is_refused(string? value)
    {
        Assert.False(EntityTags.TryParse(value, out _));
    }
}
