using EvidenceChain.Api.Security;
using EvidenceChain.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Refuse to boot outside Development with the published dev-only secrets.
DevSecretGuard.ThrowIfDevSecretsOutsideDevelopment(builder.Configuration, builder.Environment);

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks(); // liveness only: must never touch SQL (keeps serverless/auto-pause idle)
builder.Services.AddInfrastructure(connectionString);

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

app.MapOpenApi();
app.MapOpenApi("/openapi/{documentName}.yaml");
app.MapHealthChecks("/api/v1/health/live");
app.MapControllers();

app.Run();

// API responses are never cached by browsers, CDNs or rewrite proxies.
static Task NoStoreForApi(HttpContext context, RequestDelegate next)
{
    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.Headers.CacheControl = "no-store";
    return next(context);
}
