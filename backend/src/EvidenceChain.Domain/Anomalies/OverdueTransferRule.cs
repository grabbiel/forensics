using System.Globalization;
using EvidenceChain.Domain.Custody;

namespace EvidenceChain.Domain.Anomalies;

/// <summary>How serious a finding is.</summary>
public enum AnomalySeverity
{
    Medium,
    High,
}

/// <summary>What the transfer rule found.</summary>
public enum TransferAnomalyKind
{
    /// <summary>Still pending past the deadline.</summary>
    Overdue,

    /// <summary>Historical: accepted, but after the deadline.</summary>
    AcceptedLate,
}

/// <summary>A finding with its severity and a sentence a Supervisor can read.</summary>
public sealed record TransferAnomaly(TransferAnomalyKind Kind, AnomalySeverity Severity, TimeSpan Elapsed, TimeSpan Deadline, string Explanation);

/// <summary>
/// Specification: a transfer is anomalous when the recipient took longer than the acceptance deadline,
/// either still pending or accepted late. Severity is Medium below twice the deadline and High from there.
/// </summary>
public sealed class OverdueTransferRule
{
    private readonly TimeProvider _clock;

    public OverdueTransferRule(TimeSpan deadline, TimeProvider clock)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(deadline, TimeSpan.Zero);
        Deadline = deadline;
        _clock = clock;
    }

    /// <summary>How long a recipient has to accept.</summary>
    public TimeSpan Deadline { get; }

    /// <summary>True when <see cref="Evaluate"/> finds something.</summary>
    public bool IsSatisfiedBy(CustodyTransfer transfer) => Evaluate(transfer) is not null;

    /// <summary>The finding for <paramref name="transfer"/>, or null when it met the deadline (or was rejected).</summary>
    public TransferAnomaly? Evaluate(CustodyTransfer transfer)
    {
        var (kind, elapsed) = transfer.Status switch
        {
            TransferStatus.Pending => (TransferAnomalyKind.Overdue, _clock.GetUtcNow().UtcDateTime - transfer.RequestedAtUtc),
            TransferStatus.Accepted => (TransferAnomalyKind.AcceptedLate, transfer.DecidedAtUtc!.Value - transfer.RequestedAtUtc),
            _ => (default(TransferAnomalyKind?), TimeSpan.Zero),
        };
        if (kind is null || elapsed <= Deadline)
            return null;

        var severity = elapsed < 2 * Deadline ? AnomalySeverity.Medium : AnomalySeverity.High;
        return new TransferAnomaly(kind.Value, severity, elapsed, Deadline, Explain(kind.Value, elapsed));
    }

    private string Explain(TransferAnomalyKind kind, TimeSpan elapsed)
    {
        var late = Hours(elapsed - Deadline);
        return kind == TransferAnomalyKind.Overdue
            ? $"Pendiente desde hace {Hours(elapsed)}; el plazo de aceptación es {Hours(Deadline)} ({late} de retraso)."
            : $"Aceptada {Hours(elapsed)} después de solicitarse; el plazo es {Hours(Deadline)} ({late} de retraso).";
    }

    /// <summary>Whole hours, e.g. "72 h"; under an hour shows minutes.</summary>
    private static string Hours(TimeSpan span) =>
        span.TotalHours >= 1
            ? ((int)span.TotalHours).ToString(CultureInfo.InvariantCulture) + " h"
            : ((int)span.TotalMinutes).ToString(CultureInfo.InvariantCulture) + " min";
}
