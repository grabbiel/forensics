using EvidenceChain.Application.Common;
using EvidenceChain.Application.Review;
using EvidenceChain.Domain.Anomalies;
using EvidenceChain.Domain.Custody;
using EvidenceChain.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvidenceChain.Infrastructure.Review;

internal sealed class EvidenceQueries(AppDbContext db, OverdueTransferRule overdue) : IEvidenceQueries
{
    public async Task<EvidenceDetail?> GetDetailAsync(string code, CancellationToken cancellationToken)
    {
        // One snapshot, so the custodian, the transfers and the ETag all describe the same version.
        var read = await db.InSnapshotAsync(async () =>
        {
            // The content's metadata, never its bytes.
            var evidence = await db.Evidence.AsNoTracking().Where(e => e.Code == code)
                .Select(e => new
                {
                    e.EvidenceId, e.Code, e.TypeCode, e.Description, e.CapturedAtUtc, e.RegisteredAtUtc,
                    e.RegisteredById, e.InitialCustodianId, e.CurrentCustodianId, e.EventCount,
                    e.Content.Sha256, e.Content.ByteLength, e.Content.MediaType,
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (evidence is null)
                return null;

            var inbox = await db.EvidenceInbox.AsNoTracking().Where(r => r.EvidenceId == evidence.EvidenceId)
                .Select(r => new { r.LastEventAtUtc, r.IntegrityStatus, r.IntegrityCheckedAtUtc, r.IntegrityCheckedThroughSeq })
                .SingleAsync(cancellationToken);
            var transfers = await db.CustodyTransfers.AsNoTracking().Where(t => t.EvidenceId == evidence.EvidenceId)
                .OrderBy(t => t.RequestedAtUtc).ThenBy(t => t.TransferId).ToListAsync(cancellationToken);
            return new { Evidence = evidence, Inbox = inbox, Transfers = transfers };
        }, cancellationToken);
        if (read is null)
            return null;

        var (evidence, inbox, transfers) = (read.Evidence, read.Inbox, read.Transfers);
        var people = await PeopleAsync(cancellationToken);

        var pending = transfers.SingleOrDefault(t => t.Status == TransferStatus.Pending);
        var anomalies = transfers
            .Select(t => (Transfer: t, Finding: overdue.Evaluate(t)))
            .Where(x => x.Finding is not null)
            .Select(x => new AnomalyView(x.Transfer.TransferId, x.Finding!.Kind, x.Finding.Severity, x.Transfer.RequestedAtUtc,
                (long)x.Finding.Elapsed.TotalSeconds, (long)x.Finding.Deadline.TotalSeconds, x.Finding.Explanation))
            .ToList();

        return new EvidenceDetail(
            evidence.Code, evidence.TypeCode, evidence.Description, evidence.CapturedAtUtc, evidence.RegisteredAtUtc,
            people.Of(evidence.RegisteredById), people.Of(evidence.InitialCustodianId), people.Of(evidence.CurrentCustodianId),
            evidence.EventCount, inbox.LastEventAtUtc,
            new ContentSummary(Convert.ToHexStringLower(evidence.Sha256), evidence.ByteLength, evidence.MediaType),
            new IntegritySummary(inbox.IntegrityStatus, inbox.IntegrityCheckedAtUtc, inbox.IntegrityCheckedThroughSeq),
            pending is null ? null : new TransferView(pending.TransferId, pending.Status, people.Of(pending.FromCustodianId),
                people.Of(pending.ToCustodianId), people.Of(pending.RequestedById), pending.RequestedAtUtc, pending.Reason,
                EntityTags.Format(pending.RowVersion)),
            anomalies);
    }

    public async Task<EvidenceChainView?> GetChainAsync(string code, CancellationToken cancellationToken)
    {
        var evidenceId = await db.Evidence.Where(e => e.Code == code).Select(e => (long?)e.EvidenceId).SingleOrDefaultAsync(cancellationToken);
        if (evidenceId is null)
            return null;

        var events = await db.CustodyEvents.AsNoTracking().Where(e => e.EvidenceId == evidenceId).OrderBy(e => e.Seq).ToListAsync(cancellationToken);
        var people = await PeopleAsync(cancellationToken);
        return new EvidenceChainView(code, events.Select(e => new ChainEventView(
            e.CustodyEventId, e.Seq, e.Kind, e.OccurredAtUtc, people.Of(e.ActorId),
            e.FromCustodianId is { } from ? people.Of(from) : null,
            e.ToCustodianId is { } to ? people.Of(to) : null,
            e.TransferId, e.Notes, e.KeyId, Convert.ToHexStringLower(e.Mac),
            e.PrevMac is { } prev ? Convert.ToHexStringLower(prev) : null)).ToList());
    }

    /// <summary>Every user by id; there are a handful.</summary>
    private async Task<People> PeopleAsync(CancellationToken cancellationToken) =>
        new(await db.Users.AsNoTracking().ToDictionaryAsync(u => u.UserId, u => new PersonRef(u.UserId, u.DisplayName), cancellationToken));

    private sealed class People(Dictionary<int, PersonRef> byId)
    {
        public PersonRef Of(int id) => byId.TryGetValue(id, out var person) ? person : new PersonRef(id, $"#{id}");
    }
}
