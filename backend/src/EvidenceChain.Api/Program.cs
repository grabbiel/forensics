using System.Text.Json;
using System.Text.Json.Serialization;
using EvidenceChain.Api.Auth;
using EvidenceChain.Api.OpenApi;
using EvidenceChain.Api.Problems;
using EvidenceChain.Api.Security;
using EvidenceChain.Application.Anomalies;
using EvidenceChain.Domain.Anomalies;
using EvidenceChain.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Refuse to boot outside Development with the published dev-only secrets.
DevSecretGuard.ThrowIfDevSecretsOutsideDevelopment(builder.Configuration, builder.Environment);

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

// Numbers are numbers (no quoted integers, integer-only OpenAPI types) and enums travel as their names.
// Validation errors are keyed by JSON names (userName, not UserName).
builder.Services.AddControllers(o =>
    {
        o.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider());
        o.Conventions.Add(new ProblemResponsesConvention());
    })
    .AddJsonOptions(o => ConfigureJson(o.JsonSerializerOptions));
builder.Services.ConfigureHttpJsonOptions(o => ConfigureJson(o.SerializerOptions));
builder.Services.AddEvidenceChainProblems();
// A lambda, not a method group: the XML-comment source generator only intercepts lambdas.
builder.Services.AddOpenApi(options => OpenApiDocumentSetup.Configure(options));
builder.Services.AddHealthChecks(); // liveness only: must never touch SQL (keeps serverless/auto-pause idle)
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddEvidenceChainAuth();

// Anomaly rules read the deadline from configuration and the time from an injectable clock.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<AnomalyOptions>()
    .BindConfiguration(AnomalyOptions.Section)
    .Validate(o => o.TransferAcceptanceDeadline > TimeSpan.Zero, "Anomalies:TransferAcceptanceDeadline must be positive.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => new OverdueTransferRule(
    sp.GetRequiredService<IOptions<AnomalyOptions>>().Value.TransferAcceptanceDeadline, sp.GetRequiredService<TimeProvider>()));

string[] corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("ETag", "Location")));

var app = builder.Build();

app.UseExceptionHandler(); // unhandled errors → application/problem+json
app.UseStatusCodePages();  // empty 4xx/5xx → application/problem+json
app.Use(NoStoreForApi);
app.UseCors();
app.UseAuthentication();
app.UseAuthorization(); // secure by default: only what is marked AllowAnonymous skips it

app.MapOpenApi().AllowAnonymous();
app.MapOpenApi("/openapi/{documentName}.yaml").AllowAnonymous();
app.MapHealthChecks("/api/v1/health/live").AllowAnonymous();
app.MapControllers();

app.Run();

static void ConfigureJson(JsonSerializerOptions options)
{
    options.NumberHandling = JsonNumberHandling.Strict;
    options.Converters.Add(new JsonStringEnumConverter());
}

// API responses are never cached by browsers, CDNs or rewrite proxies.
static Task NoStoreForApi(HttpContext context, RequestDelegate next)
{
    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.Headers.CacheControl = "no-store";
    return next(context);
}
