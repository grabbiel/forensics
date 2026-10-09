namespace EvidenceChain.Application.Anomalies;

/// <summary>Bound from the "Anomalies" configuration section.</summary>
public sealed class AnomalyOptions
{
    public const string Section = "Anomalies";

    /// <summary>How long a recipient has to accept a transfer before it counts as overdue.</summary>
    public TimeSpan TransferAcceptanceDeadline { get; set; } = TimeSpan.FromHours(48);
}
