using System.Net.Http.Json;
using System.Text.Json;
using EvidenceChain.Application.People;
using EvidenceChain.Domain.People;
using EvidenceChain.SyntheticData;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EvidenceChain.IntegrationTests;

/// <summary>Transfer requests and decisions through the API, on evidence of the reference dataset that has none pending.</summary>
internal static class CustodyWrites
{
    private const string Transfers = "/api/v1/custody-transfers";
    private static SyntheticDataset Data => ReferenceData.Dataset.Value;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>The index-th evidence that is no fixture and has no pending transfer, its holder, and a custodian to send it to.</summary>
    public static (string Code, UserSummary Holder, UserSummary Recipient) FreeEvidence(int index)
    {
        var pending = Data.Transfers.Where(t => t.Status == SyntheticTransferStatus.Pending).Select(t => t.EvidenceCode).ToHashSet();
        var evidence = Data.Evidences.Where(e => e.Fixture is null && !pending.Contains(e.Code)).ElementAt(index * 7);
        var holder = User(evidence.CurrentCustodian);
        var recipient = User(SyntheticPeople.WithRole(SyntheticRole.Custodio).First(u => u != holder.UserName));
        return (evidence.Code, holder, recipient);
    }

    public static UserSummary User(string userName)
    {
        var user = SyntheticPeople.All.Single(u => u.UserName == userName);
        return new UserSummary(user.Id, user.UserName, user.DisplayName, Enum.Parse<UserRole>(user.Role.ToString()));
    }

    public static Task<HttpResponseMessage> RequestAsync(
        WebApplicationFactory<Program> api, string code, UserSummary recipient, string reason, Guid key, UserSummary? as_ = null) =>
        SendAsync(api.CreateClientAs(as_ ?? TestUsers.Investigator), Transfers, new { evidenceCode = code, toCustodianId = recipient.Id, reason }, key, ifMatch: null);

    public static Task<HttpResponseMessage> DecideAsync(
        WebApplicationFactory<Program> api, UserSummary decider, long id, string decision, object? body, Guid key, string? ifMatch) =>
        SendAsync(api.CreateClientAs(decider), $"{Transfers}/{id}/{decision}", body, key, ifMatch);

    /// <summary>A request that must succeed: its transfer id and ETag.</summary>
    public static async Task<(long Id, string ETag)> RequestedAsync(WebApplicationFactory<Program> api, string code, UserSummary recipient, string reason)
    {
        var response = await RequestAsync(api, code, recipient, reason, Guid.NewGuid());
        response.EnsureSuccessStatusCode();
        var transfer = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        return (transfer.GetProperty("transferId").GetInt64(), transfer.GetProperty("etag").GetString()!);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, object? body, Guid key, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key.ToString());
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(request, Token);
    }
}
