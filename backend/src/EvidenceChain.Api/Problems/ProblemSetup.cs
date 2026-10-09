using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EvidenceChain.Api.Problems;

/// <summary>Every 4xx and 5xx is application/problem+json with a trace id; validation failures share one type.</summary>
public static class ProblemSetup
{
    public static IServiceCollection AddEvidenceChainProblems(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            if (context.ProblemDetails is HttpValidationProblemDetails)
                context.ProblemDetails.Type = ProblemTypes.Validation;
            context.ProblemDetails.Extensions.TryAdd("traceId", Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
        });
        services.Configure<MvcOptions>(options => options.Filters.Add<ProblemExceptionFilter>());
        return services;
    }
}
