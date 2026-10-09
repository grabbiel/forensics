using EvidenceChain.Domain.Catalog;

namespace EvidenceChain.UnitTests.Catalog;

public sealed class EvidenceCodeTests
{
    [Theory]
    [InlineData("EML", 3, "EML202610070003")]
    [InlineData("LOG", 1, "LOG202610070001")]
    [InlineData("CSV", 9999, "CSV202610079999")]
    public void Format_builds_type_date_and_four_digit_index(string type, int dailyNo, string expected) =>
        Assert.Equal(expected, EvidenceCode.Format(type, new DateOnly(2026, 10, 7), dailyNo));

    [Theory]
    [InlineData(0)]
    [InlineData(10_000)] // "D4" would silently print five digits
    public void Format_rejects_indexes_outside_1_to_9999(int dailyNo) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => EvidenceCode.Format("LOG", new DateOnly(2026, 10, 7), dailyNo));

    [Theory]
    [InlineData("PDF")]
    [InlineData("log")]
    public void Format_rejects_unknown_types(string type) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => EvidenceCode.Format(type, new DateOnly(2026, 10, 7), 1));
}
