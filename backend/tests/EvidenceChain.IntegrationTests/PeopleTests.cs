using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EvidenceChain.Domain.People;
using EvidenceChain.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EvidenceChain.IntegrationTests;

/// <summary>The people list behind the custodian filter and the transfer recipient picker.</summary>
[Collection(nameof(SqlCollection))]
public sealed class PeopleTests(ApiFactory factory)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lists_everyone_or_one_role_in_the_database_s_name_order_without_user_names()
    {
        var api = await factory.ReferenceApiAsync();
        var client = api.CreateClientAs(TestUsers.Investigator);

        var custodians = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/people?role=Custodio", Token);
        var everyone = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/people", Token);

        // The order SQL Server's collation gives, so the test does not depend on the test host's culture.
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expected = await db.Users.Where(u => u.Role == UserRole.Custodio).OrderBy(u => u.DisplayName).ThenBy(u => u.UserId).Select(u => u.UserId).ToListAsync(Token);
        Assert.Equal(expected, custodians!.Select(p => p.GetProperty("id").GetInt32()));
        Assert.All(custodians!, p => Assert.Equal("Custodio", p.GetProperty("role").GetString()));
        Assert.Equal(await db.Users.CountAsync(Token), everyone!.Length);
        Assert.All(everyone, p => Assert.False(p.TryGetProperty("userName", out _)));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("custodio")]
    [InlineData("1")]
    public async Task A_role_not_spelled_exactly_is_a_validation_problem(string role)
    {
        var response = await (await factory.ReferenceApiAsync()).CreateClientAs(TestUsers.Investigator).GetAsync($"/api/v1/people?role={role}", Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Signing_in_is_required()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await (await factory.ReferenceApiAsync()).CreateClient().GetAsync("/api/v1/people", Token)).StatusCode);
    }
}
