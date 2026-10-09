using System.Net.Http.Headers;
using EvidenceChain.Api.Auth;
using EvidenceChain.Application.People;
using EvidenceChain.Domain.People;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EvidenceChain.IntegrationTests;

/// <summary>Seeded demo users, and clients signed in as them with the API's own issuer (no database round trip).</summary>
public static class TestUsers
{
    public static readonly UserSummary Investigator = new(1, "investigador.demo", "Lucía Ferrer", UserRole.Investigador);
    public static readonly UserSummary Custodian = new(4, "custodio.demo", "Diego Salas", UserRole.Custodio);
    public static readonly UserSummary OtherCustodian = new(5, "nuria.paredes", "Nuria Paredes", UserRole.Custodio);
    public static readonly UserSummary Supervisor = new(10, "supervisor.demo", "Elena Ruiz", UserRole.Supervisor);

    public static HttpClient CreateClientAs(this WebApplicationFactory<Program> factory, UserSummary user)
    {
        var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<TokenIssuer>().Issue(user);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }
}
