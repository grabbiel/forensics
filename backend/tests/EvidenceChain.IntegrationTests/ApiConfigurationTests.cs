using EvidenceChain.Domain.Anomalies;
using Microsoft.Extensions.DependencyInjection;

namespace EvidenceChain.IntegrationTests;

/// <summary>Settings that reach the domain through the API host.</summary>
[Collection(nameof(SqlCollection))]
public sealed class ApiConfigurationTests(ApiFactory factory)
{
    [Fact]
    public void The_acceptance_deadline_is_configurable()
    {
        Assert.Equal(TimeSpan.FromHours(48), factory.Services.GetRequiredService<OverdueTransferRule>().Deadline);

        using var threeDays = factory.WithWebHostBuilder(b => b.UseSetting("Anomalies:TransferAcceptanceDeadline", "3.00:00:00"));
        Assert.Equal(TimeSpan.FromHours(72), threeDays.Services.GetRequiredService<OverdueTransferRule>().Deadline);
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")] // 16 bytes
    [InlineData("not base64")]
    public void A_short_or_malformed_signing_key_stops_the_host_from_starting(string key)
    {
        using var weak = factory.WithWebHostBuilder(b => b.UseSetting("Jwt:SigningKey", key));
        var error = Assert.ThrowsAny<Exception>(() => weak.Services);
        Assert.Contains("Jwt:SigningKey must be base64 of at least 32 bytes", error.ToString());
    }

    [Fact]
    public void A_non_positive_deadline_stops_the_host_from_starting()
    {
        using var zero = factory.WithWebHostBuilder(b => b.UseSetting("Anomalies:TransferAcceptanceDeadline", "00:00:00"));
        var error = Assert.ThrowsAny<Exception>(() => zero.Services);
        Assert.Contains("TransferAcceptanceDeadline must be positive", error.ToString());
    }
}
