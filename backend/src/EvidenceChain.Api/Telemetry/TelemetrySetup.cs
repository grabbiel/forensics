using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using EvidenceChain.Application.Telemetry;
using OpenTelemetry.Instrumentation.AspNetCore;

namespace EvidenceChain.Api.Telemetry;

public static class TelemetrySetup
{
    /// <summary>
    /// OpenTelemetry with the EvidenceChain meter, exported to Azure Monitor only where APPLICATIONINSIGHTS_CONNECTION_STRING
    /// is set (App Service); locally nothing is exported.
    /// </summary>
    public static IServiceCollection AddEvidenceChainTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<EvidenceChainMetrics>();
        var telemetry = services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(EvidenceChainMetrics.MeterName));
        if (!string.IsNullOrWhiteSpace(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
            telemetry.UseAzureMonitor();

        // After the distro's own configuration, keeping any enrichment it set.
        services.PostConfigure<AspNetCoreTraceInstrumentationOptions>(options =>
        {
            var previous = options.EnrichWithHttpResponse;
            options.EnrichWithHttpResponse = (activity, response) =>
            {
                previous?.Invoke(activity, response);
                MarkClientErrorsAsHandled(activity, response);
            };
        });
        return services;
    }

    /// <summary>
    /// A 4xx is the API answering a client mistake (401, 404, 409, 422...), not a failure of the service. The Azure Monitor
    /// exporter counts any status of 400 or more as a failed request unless the span's status is set, so set it to Ok.
    /// </summary>
    internal static void MarkClientErrorsAsHandled(Activity activity, HttpResponse response)
    {
        if (response.StatusCode is >= 400 and < 500)
            activity.SetStatus(ActivityStatusCode.Ok);
    }
}
