using System.Diagnostics;
using System.Diagnostics.Metrics;
using EvidenceChain.Application.Telemetry;
using EvidenceChain.Domain.Integrity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Trace;

namespace EvidenceChain.IntegrationTests;

/// <summary>The verify-failure counter and how requests are reported.</summary>
[Collection(nameof(SqlCollection))]
public sealed class TelemetryTests(ApiFactory factory)
{
    [Fact]
    public void A_broken_chain_is_counted_once_with_whether_it_is_a_fixture_and_why()
    {
        var meterFactory = factory.Services.GetRequiredService<IMeterFactory>();
        var measurements = new List<(long Value, string Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            // Only this host's meter: other test hosts run in parallel.
            if (instrument.Meter.Scope == meterFactory && instrument.Name == EvidenceChainMetrics.VerifyFailures)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            measurements.Add((value, string.Join(" ", tags.ToArray().Select(t => $"{t.Key}={t.Value}").Order()))));
        listener.Start();

        var metrics = factory.Services.GetRequiredService<EvidenceChainMetrics>();
        metrics.RecordVerifyFailure(ChainFailure.MacMismatch, fixture: true);
        metrics.RecordVerifyFailure(ChainFailure.ContentHashMismatch, fixture: false);

        Assert.Equal([(1L, "fixture=True reason=MAC_MISMATCH"), (1L, "fixture=False reason=CONTENT_HASH_MISMATCH")], measurements);
    }

    [Fact]
    public void Client_errors_are_not_reported_as_failed_requests()
    {
        AssertRequestStatuses(factory);
    }

    [Fact]
    public void With_application_insights_configured_the_exporter_starts_and_client_errors_stay_successes()
    {
        // A local endpoint, so nothing leaves the machine.
        const string connectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://localhost:1/;LiveEndpoint=https://localhost:1/";
        using var azure = factory.WithWebHostBuilder(b => b.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING", connectionString));

        Assert.NotNull(azure.Services.GetService<TracerProvider>());
        AssertRequestStatuses(azure);
    }

    /// <summary>What the Azure Monitor exporter reads to decide success: a 4xx span set to Ok, the rest left alone.</summary>
    private static void AssertRequestStatuses(WebApplicationFactory<Program> host)
    {
        var options = host.Services.GetRequiredService<IOptionsMonitor<AspNetCoreTraceInstrumentationOptions>>().Get(Options.DefaultName);
        foreach (var (status, expected) in new[] { (200, ActivityStatusCode.Unset), (404, ActivityStatusCode.Ok), (409, ActivityStatusCode.Ok), (500, ActivityStatusCode.Unset) })
        {
            using var activity = new Activity("request");
            var context = new DefaultHttpContext();
            context.Response.StatusCode = status;

            options.EnrichWithHttpResponse!(activity, context.Response);

            Assert.Equal(expected, activity.Status);
        }
    }
}
