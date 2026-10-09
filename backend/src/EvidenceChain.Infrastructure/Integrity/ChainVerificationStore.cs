using EvidenceChain.Application.Integrity;
using EvidenceChain.Domain.Custody;
using EvidenceChain.Domain.Integrity;
using EvidenceChain.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvidenceChain.Infrastructure.Integrity;

internal sealed class ChainVerificationStore(AppDbContext db) : IChainVerificationStore
{
    /// <summary>The evidence, its events and its transfers as of one instant.</summary>
    public Task<VerificationInput?> LoadAsync(string code, CancellationToken cancellationToken) =>
        db.InSnapshotAsync(async () =>
        {
            var evidence = await db.Evidence.AsNoTracking().Include(e => e.Content).SingleOrDefaultAsync(e => e.Code == code, cancellationToken);
            if (evidence is null)
                return null;

            var chain = await db.CustodyEvents.AsNoTracking().Where(e => e.EvidenceId == evidence.EvidenceId).OrderBy(e => e.Seq).ToListAsync(cancellationToken);
            var transfers = await db.CustodyTransfers.AsNoTracking().Where(t => t.EvidenceId == evidence.EvidenceId).ToListAsync(cancellationToken);
            return new VerificationInput(evidence, chain, transfers);
        }, cancellationToken);

    public async Task<bool> RecordAsync(long evidenceId, int eventCount, ChainVerdict verdict, DateTime checkedAtUtc, CancellationToken cancellationToken)
    {
        var status = verdict.IsValid ? IntegrityStatus.Valid : IntegrityStatus.Invalid;
        var recorded = await db.EvidenceInbox
            .Where(r => r.EvidenceId == evidenceId && r.EventCount == eventCount
                && (r.IntegrityCheckedAtUtc == null || r.IntegrityCheckedAtUtc <= checkedAtUtc))
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.IntegrityStatus, status)
                .SetProperty(r => r.IntegrityCheckedAtUtc, checkedAtUtc)
                .SetProperty(r => r.IntegrityCheckedThroughSeq, verdict.VerifiedThroughSeq), cancellationToken);
        return recorded == 1;
    }

    /// <summary>Seeks IX_EvidenceInbox_IntegrityChecked; SQL Server sorts NULL (never checked) first.</summary>
    public async Task<IReadOnlyList<string>> StalestAsync(int count, CancellationToken cancellationToken) =>
        await db.EvidenceInbox.AsNoTracking()
            .OrderBy(r => r.IntegrityCheckedAtUtc).ThenBy(r => r.EvidenceId)
            .Take(count).Select(r => r.Code).ToListAsync(cancellationToken);
}
