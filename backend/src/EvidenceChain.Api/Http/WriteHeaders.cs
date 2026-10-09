using EvidenceChain.Api.Problems;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace EvidenceChain.Api.Http;

/// <summary>
/// The write needs one Idempotency-Key header holding a UUID; without it the answer is a 400 validation problem
/// listing it with any other invalid field. Read the key with <see cref="WriteHeaders.IdempotencyKey"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireIdempotencyKeyAttribute : Attribute, IActionFilter, IOrderedFilter
{
    public const string Header = "Idempotency-Key";

    /// <summary>Before [ApiController]'s automatic 400 (-2000), so one response lists every problem.</summary>
    public int Order => -3000;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var values = context.HttpContext.Request.Headers[Header];
        if (values.Count == 1 && Guid.TryParse(values[0], out var key) && key != Guid.Empty)
        {
            context.HttpContext.Items[WriteHeaders.IdempotencyKeyItem] = key;
            return;
        }

        context.ModelState.AddModelError(Header, "Send one Idempotency-Key header with a new UUID for each distinct write; repeat it only to retry.");
        context.Result = WriteHeaders.ValidationProblem(context);
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}

/// <summary>
/// The write needs If-Match with the ETag last read: 428 when it is missing, a 400 validation problem when it is not
/// one strong ETag of this API. Read the version with <see cref="WriteHeaders.IfMatchVersion"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireIfMatchAttribute : Attribute, IActionFilter, IOrderedFilter
{
    public const string Header = "If-Match";

    public int Order => -3000;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var http = context.HttpContext;
        var values = http.Request.Headers.IfMatch;
        if (string.IsNullOrWhiteSpace(values))
        {
            var problems = http.RequestServices.GetRequiredService<ProblemDetailsFactory>();
            var problem = problems.CreateProblemDetails(http, StatusCodes.Status428PreconditionRequired, "If-Match required.",
                ProblemTypes.PreconditionRequired, "Send If-Match with the ETag of the transfer as you last read it.");
            context.Result = new ObjectResult(problem) { StatusCode = problem.Status, ContentTypes = { "application/problem+json" } };
            return;
        }

        if (values.Count == 1 && EntityTags.TryParse(values[0], out var version))
        {
            http.Items[WriteHeaders.IfMatchItem] = version;
            return;
        }

        context.ModelState.AddModelError(Header, "Send the one strong ETag this API returned, quotes included.");
        context.Result = WriteHeaders.ValidationProblem(context);
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

    /// <summary>The same 400 [ApiController] would give, with the header errors next to any binding errors.</summary>
    internal static IActionResult ValidationProblem(ActionExecutingContext context)
    {
        var problems = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = problems.CreateValidationProblemDetails(context.HttpContext, context.ModelState);
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }
}
