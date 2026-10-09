using EvidenceChain.Api.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace EvidenceChain.UnitTests.Security;

public sealed class DevSecretGuardTests
{
    // Same value as appsettings.Development.json: base64 of "DEV-ONLY-…".
    private const string DevJwtKey = "REVWLU9OTFktTk9ULUEtU0VDUkVULUpXVC1TSUdOSU5HLUtFWS1FVklERU5DRS1DSEFJTi0yMDI2";

    [Fact]
    public void Throws_in_Production_when_a_dev_secret_is_configured()
    {
        var config = Config(("Jwt:SigningKey", DevJwtKey));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DevSecretGuard.ThrowIfDevSecretsOutsideDevelopment(config, new FakeEnvironment(Environments.Production)));
        Assert.Contains("Jwt:SigningKey", ex.Message);
    }

    [Fact]
    public void Allows_dev_secrets_in_Development() =>
        DevSecretGuard.ThrowIfDevSecretsOutsideDevelopment(
            Config(("Jwt:SigningKey", DevJwtKey)), new FakeEnvironment(Environments.Development));

    [Fact]
    public void Allows_real_secrets_in_Production() =>
        DevSecretGuard.ThrowIfDevSecretsOutsideDevelopment(
            Config(("Jwt:SigningKey", Convert.ToBase64String(new byte[48])), ("Integrity:Keys:k1", "c2VjcmV0LWZyb20ta2V5LXZhdWx0")),
            new FakeEnvironment(Environments.Production));

    [Fact]
    public void Finds_dev_integrity_keys_and_dev_database_passwords()
    {
        var config = Config(
            ("Integrity:Keys:dev", "REVWLU9OTFktTk9ULUEtU0VDUkVULUhNQUMtQ0hBSU4tS0VZLUVWSURFTkNFLUNIQUlOLTIwMjY="),
            ("ConnectionStrings:Default", "Server=x;User Id=evidence_app;Password=DevOnly_AppPassw0rd!2026"));

        Assert.Equal(["ConnectionStrings:Default", "Integrity:Keys:dev"], DevSecretGuard.FindDevSecrets(config).Order());
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "EvidenceChain.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
