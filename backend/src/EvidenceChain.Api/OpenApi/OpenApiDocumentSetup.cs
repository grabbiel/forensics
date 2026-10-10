using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using EvidenceChain.Api.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
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

            PublishAllowedValues(operation, context);
            DeclareResponseHeaders(operation, metadata);

            if (metadata.OfType<RequireIdempotencyKeyAttribute>().Any())
                AddHeader(operation, RequireIdempotencyKeyAttribute.Header, "uuid",
                    "A new UUID for each distinct write; resend the same one to retry safely.");
            if (metadata.OfType<RequireIfMatchAttribute>().Any())
                AddHeader(operation, RequireIfMatchAttribute.Header, null,
                    "The ETag of the resource as last read, quotes included; a newer version answers 409.");
            return Task.CompletedTask;
        });
    }

    /// <summary>A parameter limited by [AllowedValues] lists them as its enum, so clients see the choices.</summary>
    private static void PublishAllowedValues(OpenApiOperation operation, OpenApiOperationTransformerContext context)
    {
        foreach (var description in context.Description.ParameterDescriptions)
        {
            var allowed = (description.ParameterDescriptor as ControllerParameterDescriptor)?.ParameterInfo
                .GetCustomAttributes(typeof(AllowedValuesAttribute), inherit: false).OfType<AllowedValuesAttribute>().SingleOrDefault();
            var parameter = operation.Parameters?.FirstOrDefault(p => p.Name == description.Name);
            if (allowed is null || parameter?.Schema is not OpenApiSchema schema)
                continue;
            schema.Enum = allowed.Values.OfType<string>().Select(v => (JsonNode)JsonValue.Create(v)).ToList();
        }
    }

    /// <summary>The headers a response carries: ETag, the replay flag on idempotent writes, Location on 201, Retry-After on 429.</summary>
    private static void DeclareResponseHeaders(OpenApiOperation operation, IList<object> metadata)
    {
        var etag = metadata.OfType<ReturnsETagAttribute>().Any();
        var replayable = metadata.OfType<RequireIdempotencyKeyAttribute>().Any();
        foreach (var (status, response) in operation.Responses ?? [])
        {
            if (status == "429" && response is OpenApiResponse throttled)
            {
                throttled.Headers ??= new Dictionary<string, IOpenApiHeader>();
                throttled.Headers["Retry-After"] = Header("Seconds to wait before trying again; nothing was done.", JsonSchemaType.Integer);
            }
            if (!status.StartsWith('2') || response is not OpenApiResponse success)
                continue;
            success.Headers ??= new Dictionary<string, IOpenApiHeader>();
            if (etag)
                success.Headers["ETag"] = Header("The resource's version; send it back as If-Match.");
            if (replayable)
                success.Headers["Idempotent-Replayed"] = Header("\"true\" when this answers an earlier request with the same Idempotency-Key.");
            if (status == "201")
                success.Headers["Location"] = Header("Where the created resource can be read.");
        }

        static OpenApiHeader Header(string description, JsonSchemaType type = JsonSchemaType.String) =>
            new() { Description = description, Schema = new OpenApiSchema { Type = type } };
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
