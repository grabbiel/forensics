using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EvidenceChain.Api.Http;

/// <summary>
/// What makes two writes "the same request" for idempotency: SHA-256 over the method, the path, the command as the
/// service will act on it (trimmed, normalized) and the If-Match version. Spelling differences that change nothing,
/// such as whitespace, path casing or hex casing, keep the fingerprint; anything else under the same key is a 422.
/// </summary>
public static class RequestFingerprint
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static byte[] Of(HttpContext http, object command)
    {
        var version = http.Items.ContainsKey(WriteHeaders.IfMatchItem) ? Convert.ToHexStringLower(http.IfMatchVersion()) : "";
        var canonical = string.Join('\n',
            http.Request.Method, http.Request.Path.Value?.ToLowerInvariant(), JsonSerializer.Serialize(command, command.GetType(), Json), version);
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }
}
