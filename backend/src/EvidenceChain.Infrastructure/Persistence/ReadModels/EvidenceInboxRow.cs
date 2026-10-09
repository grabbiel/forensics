using EvidenceChain.Domain.Custody;

namespace EvidenceChain.Infrastructure.Persistence.ReadModels;

/// <summary>
/// EvidenceInbox projection: one denormalised row per evidence, updated in the same transaction as every write,
/// so the inbox pages with keyset seeks and no joins.
/// </summary>
public sealed class EvidenceInboxRow
{
    public long EvidenceId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string TypeCode { get; set; } = string.Empty;

    public DateOnly CodeDateUtc { get; set; }

    public string Description { get; set; } = string.Empty;

    public DateTime RegisteredAtUtc { get; set; }

    public int CurrentCustodianId { get; set; }

    public string CurrentCustodianName { get; set; } = string.Empty;

    public int EventCount { get; set; }

    /// <summary>Time of the newest custody event; the inbox's sort key.</summary>
    public DateTime LastEventAtUtc { get; set; }

    public IntegrityStatus IntegrityStatus { get; set; }

    public DateTime? IntegrityCheckedAtUtc { get; set; }

    /// <summary>Highest sequence covered by the last verification.</summary>
    public int? IntegrityCheckedThroughSeq { get; set; }

    public long? PendingTransferId { get; set; }

    public int? PendingToCustodianId { get; set; }

    public DateTime? PendingSinceUtc { get; set; }
}
