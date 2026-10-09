using EvidenceChain.Api.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace EvidenceChain.Api.OpenApi;

/// <summary>
/// Declares the problem responses that cross-cutting rules can produce, read from the same attributes that
/// enforce them, so the OpenAPI document cannot drift from the behaviour: 401 unless anonymous, 403 behind a
/// policy or role, 400 for header validation and 428 for a missing If-Match.
/// </summary>
internal sealed class ProblemResponsesConvention : IActionModelConvention
{
    private const string ProblemJson = "application/problem+json";

    public void Apply(ActionModel action)
    {
        var attributes = action.Controller.Attributes.Concat(action.Attributes).ToArray();
        if (!attributes.OfType<IAllowAnonymous>().Any())
        {
            Add(action, typeof(ProblemDetails), StatusCodes.Status401Unauthorized);
            if (attributes.OfType<IAuthorizeData>().Any(a => a.Policy is not null || a.Roles is not null))
                Add(action, typeof(ProblemDetails), StatusCodes.Status403Forbidden);
        }

        if (attributes.OfType<RequireIdempotencyKeyAttribute>().Any() || attributes.OfType<RequireIfMatchAttribute>().Any())
            Add(action, typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest);
        if (attributes.OfType<RequireIfMatchAttribute>().Any())
            Add(action, typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired);
    }

    /// <summary>Unless the action already declares that status itself.</summary>
    private static void Add(ActionModel action, Type type, int status)
    {
        if (!action.Filters.Concat(action.Attributes).OfType<IApiResponseMetadataProvider>().Any(p => p.StatusCode == status))
            action.Filters.Add(new ProducesResponseTypeAttribute(type, status, ProblemJson));
    }
}
