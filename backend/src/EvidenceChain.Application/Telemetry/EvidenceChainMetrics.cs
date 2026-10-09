using System.Diagnostics.Metrics;
using EvidenceChain.Domain.Integrity;

namespace EvidenceChain.Application.Telemetry;

/// <summary>The "EvidenceChain" meter. The first operational alert watches verify failures outside the demo fixtures.</summary>
public sealed class EvidenceChainMetrics
{
    public const string MeterName = "EvidenceChain";
    public const string VerifyFailures = "evidence.chain.verify.failures";

    private readonly Counter<long> _verifyFailures;

    public EvidenceChainMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _verifyFailures = meter.CreateCounter<long>(VerifyFailures, unit: "{failure}", description: "Chain verifications that found a broken chain.");
    }

    /// <summary>Counts a broken chain; <paramref name="fixture"/> marks the seeded tamper demos, which alerts ignore.</summary>
    public void RecordVerifyFailure(ChainFailure failure, bool fixture) =>
        _verifyFailures.Add(1, new KeyValuePair<string, object?>("fixture", fixture), new KeyValuePair<string, object?>("reason", failure.Code()));
}
