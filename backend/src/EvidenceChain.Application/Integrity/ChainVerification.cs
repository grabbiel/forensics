using EvidenceChain.Application.Telemetry;
using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.Custody;
using EvidenceChain.Domain.Integrity;

namespace EvidenceChain.Application.Integrity;

/// <summary>Everything a verification reads: the evidence with its content, its events in order and its transfer rows.</summary>
public sealed record VerificationInput(Evidence Evidence, IReadOnlyList<CustodyEvent> Chain, IReadOnlyList<CustodyTransfer> Transfers);

public interface IChainVerificationStore
{
    Task<VerificationInput?> LoadAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Records the verdict in the inbox projection unless the chain has grown since it was loaded or a later check is
    /// already recorded; true when it was recorded.
    /// </summary>
    Task<bool> RecordAsync(long evidenceId, int eventCount, ChainVerdict verdict, DateTime checkedAtUtc, CancellationToken cancellationToken);

    /// <summary>Codes of the evidence checked longest ago, never-checked first.</summary>
    Task<IReadOnlyList<string>> StalestAsync(int count, CancellationToken cancellationToken);
}

/// <summary>Where verification first failed: the event at that position (when there is one), its sequence and why.</summary>
public sealed record InvalidEvent(long? EventId, int Seq, string Reason, string Detail);

public sealed record VerificationReport(string Code, bool Valid, int VerifiedThroughSeq, int EventCount, DateTime CheckedAtUtc, InvalidEvent? FirstInvalid);

/// <summary>Verifies chains on request and in sweeps, records the outcome and counts failures.</summary>
public sealed class ChainVerificationService(IChainVerificationStore store, IntegrityKeyRing keys, EvidenceChainMetrics metrics, TimeProvider clock)
{
    /// <summary>Null when no evidence has that code.</summary>
    public async Task<VerificationReport?> VerifyAsync(string code, CancellationToken cancellationToken)
    {
        // Stamped when the read starts, not when verification ends: of two overlapping checks, the one that read later
        // wins, so a slow check of the old data cannot overwrite what a later one found.
        var checkedAt = clock.GetUtcNow().UtcDateTime;
        if (await store.LoadAsync(code, cancellationToken) is not { } input)
            return null;

        var evidence = input.Evidence;
        var verdict = ChainVerifier.Verify(keys, evidence, input.Chain, input.Transfers, evidence.Content);
        await store.RecordAsync(evidence.EvidenceId, evidence.EventCount, verdict, checkedAt, cancellationToken);

        InvalidEvent? firstInvalid = null;
        if (verdict is { IsValid: false, Failure: { } failure, FailedAtSeq: { } seq })
        {
            metrics.RecordVerifyFailure(failure, evidence.IsDemoFixture);
            firstInvalid = new InvalidEvent(input.Chain.ElementAtOrDefault(seq - 1)?.CustodyEventId, seq, failure.Code(), verdict.Detail ?? "");
        }

        return new VerificationReport(evidence.Code, verdict.IsValid, verdict.VerifiedThroughSeq, evidence.EventCount, checkedAt, firstInvalid);
    }

    /// <summary>Re-verifies the <paramref name="batchSize"/> evidence checked longest ago; returns how many were verified.</summary>
    public async Task<int> SweepAsync(int batchSize, CancellationToken cancellationToken)
    {
        var verified = 0;
        foreach (var code in await store.StalestAsync(batchSize, cancellationToken))
        {
            if (await VerifyAsync(code, cancellationToken) is not null)
                verified++;
        }
        return verified;
    }
}
