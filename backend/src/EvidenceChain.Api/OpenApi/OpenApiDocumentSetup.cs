using EvidenceChain.Api.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace EvidenceChain.Api.OpenApi;

/// <summary>Fixed document metadata, so the exported openapi.yaml is identical on every machine.</summary>
internal static class OpenApiDocumentSetup
{
    private const string BearerScheme = "Bearer";

    /// <summary>Sets title, description and servers, the bearer scheme, and per-operation security and write headers.</summary>
    public static void Configure(OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "Evidence Chain API",
                Version = "v1",
                Description = "Chain of custody for digital evidence. Errors use RFC 9457 problem details.",
            };
            document.Servers = [new OpenApiServer { Url = "http://localhost:8081", Description = "docker compose" }];
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Token from POST /api/v1/auth/token.",
            };
            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            if (!metadata.OfType<IAllowAnonymous>().Any())
                operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(BearerScheme, context.Document)] = [] }];

            if (metadata.OfType<RequireIdempotencyKeyAttribute>().Any())
                AddHeader(operation, RequireIdempotencyKeyAttribute.Header, "uuid",
                    "A new UUID for each distinct write; resend the same one to retry safely.");
            if (metadata.OfType<RequireIfMatchAttribute>().Any())
                AddHeader(operation, RequireIfMatchAttribute.Header, null,
                    "The ETag of the resource as last read, quotes included; a newer version answers 409.");
            return Task.CompletedTask;
        });
    }

    private static void AddHeader(OpenApiOperation operation, string name, string? format, string description)
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Header,
            Required = true,
            Description = description,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = format },
        });
    }
}
