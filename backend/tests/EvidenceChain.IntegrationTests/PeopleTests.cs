using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EvidenceChain.SyntheticData;

namespace EvidenceChain.IntegrationTests;

/// <summary>The people list behind the custodian filter and the transfer recipient picker.</summary>
[Collection(nameof(SqlCollection))]
public sealed class PeopleTests(ApiFactory factory)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lists_everyone_or_one_role_by_display_name()
    {
        var client = (await factory.ReferenceApiAsync()).CreateClientAs(TestUsers.Investigator);

        var custodians = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/people?role=Custodio", Token);
        var everyone = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/people", Token);

        var expected = SyntheticPeople.All.Where(u => u.Role == SyntheticRole.Custodio).Select(u => u.DisplayName).Order(StringComparer.Ordinal);
        Assert.Equal(expected, custodians!.Select(p => p.GetProperty("displayName").GetString()).Order(StringComparer.Ordinal));
        Assert.All(custodians!, p => Assert.Equal("Custodio", p.GetProperty("role").GetString()));
        Assert.Equal(SyntheticPeople.All.Count, everyone!.Length);
        var names = everyone.Select(p => p.GetProperty("displayName").GetString()!).ToList();
        Assert.Equal(names.Order(StringComparer.CurrentCulture), names); // by display name, as SQL Server collates
    }

    [Fact]
    public async Task An_unknown_role_is_a_validation_problem_and_signing_in_is_required()
    {
        var api = await factory.ReferenceApiAsync();

        var invalid = await api.CreateClientAs(TestUsers.Investigator).GetAsync("/api/v1/people?role=Admin", Token);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/api/v1/people", Token)).StatusCode);
    }
}
