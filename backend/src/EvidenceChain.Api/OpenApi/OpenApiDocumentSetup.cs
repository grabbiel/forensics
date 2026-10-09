using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace EvidenceChain.Api.OpenApi;

/// <summary>Fixed document metadata, so the exported openapi.yaml is identical on every machine.</summary>
internal static class OpenApiDocumentSetup
{
    /// <summary>Sets title, description and servers instead of the request-derived defaults.</summary>
    public static void Configure(OpenApiOptions options) =>
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "Evidence Chain API",
                Version = "v1",
                Description = "Chain of custody for digital evidence. Errors use RFC 9457 problem details.",
            };
            document.Servers = [new OpenApiServer { Url = "http://localhost:8081", Description = "docker compose" }];
            return Task.CompletedTask;
        });
}
