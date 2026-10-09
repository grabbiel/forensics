using EvidenceChain.Api.Problems;
using EvidenceChain.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace EvidenceChain.Api.Http;

/// <summary>The write needs one Idempotency-Key header holding a UUID. Read it with <see cref="WriteHeaders.IdempotencyKey"/>.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireIdempotencyKeyAttribute : Attribute
{
    public const string Header = "Idempotency-Key";
}

/// <summary>The write needs If-Match with the ETag last read. Read the version with <see cref="WriteHeaders.IfMatchVersion"/>.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireIfMatchAttribute : Attribute
{
    public const string Header = "If-Match";
}

/// <summary>
/// Enforces both attributes in one place: 428 when a required If-Match is missing; otherwise one 400 validation problem
/// listing every malformed header next to any binding error. Does not depend on [ApiController]'s automatic 400.
/// </summary>
internal sealed class WriteHeadersFilter(ProblemDetailsFactory problems) : IActionFilter
{
    /// <summary>Register with this order: before [ApiController]'s automatic 400 (-2000), so one response lists every problem.</summary>
    public const int Order = -3000;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var metadata = context.ActionDescriptor.EndpointMetadata;
        var needsKey = metadata.OfType<RequireIdempotencyKeyAttribute>().Any();
        var needsIfMatch = metadata.OfType<RequireIfMatchAttribute>().Any();
        if (!needsKey && !needsIfMatch)
            return;

        var http = context.HttpContext;
        if (needsIfMatch && string.IsNullOrWhiteSpace(http.Request.Headers.IfMatch))
        {
            var problem = problems.CreateProblemDetails(http, StatusCodes.Status428PreconditionRequired, "If-Match required.",
                ProblemTypes.PreconditionRequired, "Send If-Match with the ETag of the transfer as you last read it.");
            context.Result = new ObjectResult(problem) { StatusCode = problem.Status, ContentTypes = { "application/problem+json" } };
            return;
        }

        if (needsKey)
        {
            var keys = http.Request.Headers[RequireIdempotencyKeyAttribute.Header];
            if (keys.Count == 1 && Guid.TryParse(keys[0], out var key) && key != Guid.Empty)
                http.Items[WriteHeaders.IdempotencyKeyItem] = key;
            else
                context.ModelState.AddModelError(RequireIdempotencyKeyAttribute.Header, "Send one Idempotency-Key header with a new UUID for each distinct write; repeat it only to retry.");
        }

        if (needsIfMatch)
        {
            var tags = http.Request.Headers.IfMatch;
            if (tags.Count == 1 && EntityTags.TryParse(tags[0], out var version))
                http.Items[WriteHeaders.IfMatchItem] = version;
            else
                context.ModelState.AddModelError(RequireIfMatchAttribute.Header, "Send the one strong ETag this API returned, quotes included.");
        }

        if (!context.ModelState.IsValid)
        {
            var problem = problems.CreateValidationProblemDetails(http, context.ModelState);
            context.Result = new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}

public static class WriteHeaders
{
    internal const string IdempotencyKeyItem = "EvidenceChain.IdempotencyKey";
    internal const string IfMatchItem = "EvidenceChain.IfMatch";

    /// <summary>The validated Idempotency-Key; the action must carry [RequireIdempotencyKey].</summary>
    public static Guid IdempotencyKey(this HttpContext http) =>
        http.Items[IdempotencyKeyItem] as Guid? ?? throw new InvalidOperationException("Mark the action with [RequireIdempotencyKey].");

    /// <summary>The rowversion named by If-Match; the action must carry [RequireIfMatch].</summary>
    public static byte[] IfMatchVersion(this HttpContext http) =>
        http.Items[IfMatchItem] as byte[] ?? throw new InvalidOperationException("Mark the action with [RequireIfMatch].");
}
