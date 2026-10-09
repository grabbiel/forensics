using EvidenceChain.Application.Common;
using EvidenceChain.Application.Custody;
using EvidenceChain.Application.Idempotency;
using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.Custody;
using EvidenceChain.Domain.Integrity;
using EvidenceChain.Domain.People;
using EvidenceChain.Infrastructure.People;
using EvidenceChain.Infrastructure.Persistence;
using EvidenceChain.Infrastructure.Persistence.ReadModels;
using Microsoft.EntityFrameworkCore;

namespace EvidenceChain.Infrastructure.Custody;

/// <summary>
/// Each write is one transaction under the retrying execution strategy: the transfer change, its signed event, the
/// evidence head and the inbox projection, with the idempotency key and request fingerprint stored alongside.
/// </summary>
internal sealed class CustodyTransferService(AppDbContext db, IntegrityKeyRing keys, TimeProvider clock) : ICustodyTransfers
{
    private const string RequestKeyIndex = "UX_CustodyTransfers_RequestKey";
    private const string OnePendingIndex = "UX_CustodyTransfers_OnePendingPerEvidence";
    private const string DecisionKeyIndex = "UX_CustodyTransfers_DecisionKey";
    private const string EventSeqIndex = "UX_CustodyEvents_EvidenceSeq";
    private const int RequestAttempts = 3;

    public async Task<TransferResource?> GetAsync(long transferId, CancellationToken cancellationToken)
    {
        var transfer = await db.CustodyTransfers.AsNoTracking().SingleOrDefaultAsync(t => t.TransferId == transferId, cancellationToken);
        return transfer is null ? null : await RepresentAsync(transfer, cancellationToken);
    }

    public async Task<TransferOutcome> RequestAsync(TransferRequest request, Actor requester, Guid idempotencyKey, byte[] fingerprint, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await db.Database.CreateExecutionStrategy().ExecuteAsync(
                    () => RequestOnceAsync(request, requester, idempotencyKey, fingerprint, cancellationToken));
            }
            catch (DbUpdateException e) when (UniqueViolation.IndexOf(e) is RequestKeyIndex or OnePendingIndex)
            {
                // Another request committed first. A duplicate of ours breaks both indexes and SQL Server names either,
                // so look for our key before calling it a second pending transfer.
                db.ChangeTracker.Clear();
                if (await ReplayRequestAsync(requester, idempotencyKey, fingerprint, cancellationToken) is { } replay)
                    return replay;
                if (UniqueViolation.IndexOf(e) == RequestKeyIndex)
                    throw new IdempotencyInFlightException(idempotencyKey);
                // The winner may already be decided; then the evidence takes requests again, so try again.
                if (await PendingForAsync(request.EvidenceCode, cancellationToken) is { } pending)
                    throw new InvalidTransitionException(TransferCommand.Request, pending);
                if (attempt == RequestAttempts)
                    throw new ConcurrentWriteException(request.EvidenceCode);
            }
            catch (DbUpdateException e) when (e is DbUpdateConcurrencyException || UniqueViolation.IndexOf(e) == EventSeqIndex)
            {
                // Another write moved the evidence's head first; start again from the new one, a few times.
                db.ChangeTracker.Clear();
                if (attempt == RequestAttempts)
                    throw new ConcurrentWriteException(request.EvidenceCode);
            }
        }
    }

    public async Task<TransferOutcome> DecideAsync(
        long transferId, TransferCommand command, string? notes, Actor decider, byte[] expectedVersion, Guid idempotencyKey, byte[] fingerprint,
        CancellationToken cancellationToken)
    {
        if (command == TransferCommand.Request)
            throw new ArgumentOutOfRangeException(nameof(command), command, "Decide accepts or rejects.");

        try
        {
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(
                () => DecideOnceAsync(transferId, command, notes, decider, expectedVersion, idempotencyKey, fingerprint, cancellationToken));
        }
        catch (DbUpdateException e) when (e is DbUpdateConcurrencyException || UniqueViolation.IndexOf(e) == EventSeqIndex)
        {
            // Someone decided between our read and our save: perhaps our own retry, sent in parallel; otherwise report the
            // transfer as it is now.
            db.ChangeTracker.Clear();
            var current = await db.CustodyTransfers.AsNoTracking().SingleAsync(t => t.TransferId == transferId, cancellationToken);
            if (current.DecisionKey == idempotencyKey)
                return await ReplayDecisionAsync(current, decider, idempotencyKey, fingerprint, cancellationToken);
            throw current.Status == TransferStatus.Pending ? new StaleVersionException(current) : new InvalidTransitionException(command, current);
        }
        catch (DbUpdateException e) when (UniqueViolation.IndexOf(e) == DecisionKeyIndex)
        {
            throw new IdempotencyKeyReusedException(idempotencyKey); // already used by this decider on another transfer
        }
    }

    private async Task<TransferOutcome> RequestOnceAsync(TransferRequest request, Actor requester, Guid key, byte[] fingerprint, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear(); // a retry starts from what is committed
        if (await ReplayRequestAsync(requester, key, fingerprint, cancellationToken) is { } replay)
            return replay;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var evidence = await db.Evidence.SingleOrDefaultAsync(e => e.Code == request.EvidenceCode, cancellationToken)
            ?? throw new InvalidRequestException("evidenceCode", "No evidence has that code.");
        var recipient = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.UserId == request.ToCustodianId, cancellationToken);
        if (recipient is not { Role: UserRole.Custodio })
            throw new InvalidRequestException("toCustodianId", "Send it to a user with the Custodio role.");
        if (recipient.UserId == evidence.CurrentCustodianId)
            throw new InvalidRequestException("toCustodianId", "That custodian already holds the evidence.");
        var pending = await db.CustodyTransfers.AsNoTracking()
            .SingleOrDefaultAsync(t => t.EvidenceId == evidence.EvidenceId && t.Status == TransferStatus.Pending, cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        var transfer = CustodyTransfer.Request(evidence, requester, new Actor(recipient.UserId, recipient.Role), pending, now, request.Reason, key, fingerprint);
        db.CustodyTransfers.Add(transfer);
        await db.SaveChangesAsync(cancellationToken); // assigns the id its event cites

        db.CustodyEvents.Add(CustodyLedger.Append(keys, evidence, transfer, CustodyEventKind.TransferRequested, requester.UserId, now));
        var people = await PeopleIndex.LoadAsync(db, cancellationToken);
        Project(await InboxRowAsync(evidence, cancellationToken), evidence, transfer, now, people);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TransferOutcome(await RepresentAsync(transfer, cancellationToken), Replayed: false);
    }

    private async Task<TransferOutcome> DecideOnceAsync(
        long transferId, TransferCommand command, string? notes, Actor decider, byte[] expectedVersion, Guid key, byte[] fingerprint,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var transfer = await db.CustodyTransfers.SingleOrDefaultAsync(t => t.TransferId == transferId, cancellationToken)
            ?? throw new InvalidOperationException($"Transfer {transferId} does not exist.");

        // A retry of a decision already made: answer as it did, whatever version the retry names.
        if (transfer.DecisionKey == key)
            return await ReplayDecisionAsync(transfer, decider, key, fingerprint, cancellationToken);

        // A pending transfer only changes version when decided, so a different If-Match is an outdated read.
        if (transfer.Status == TransferStatus.Pending && !transfer.RowVersion.SequenceEqual(expectedVersion))
            throw new StaleVersionException(transfer);
        // And the save only matches the row at the version the client read.
        db.Entry(transfer).Property(t => t.RowVersion).OriginalValue = expectedVersion;

        var evidence = await db.Evidence.SingleAsync(e => e.EvidenceId == transfer.EvidenceId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        if (command == TransferCommand.Accept)
            transfer.Accept(evidence, decider, now, notes, key, fingerprint);
        else
            transfer.Reject(evidence, decider, now, notes ?? string.Empty, key, fingerprint);

        var kind = command == TransferCommand.Accept ? CustodyEventKind.TransferAccepted : CustodyEventKind.TransferRejected;
        db.CustodyEvents.Add(CustodyLedger.Append(keys, evidence, transfer, kind, decider.UserId, now));
        var people = await PeopleIndex.LoadAsync(db, cancellationToken);
        Project(await InboxRowAsync(evidence, cancellationToken), evidence, transfer, now, people);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TransferOutcome(await RepresentAsync(transfer, cancellationToken), Replayed: false);
    }

    private async Task<TransferOutcome> ReplayDecisionAsync(CustodyTransfer transfer, Actor decider, Guid key, byte[] fingerprint, CancellationToken cancellationToken) =>
        transfer.DecidedById == decider.UserId && transfer.DecisionFingerprint is { } previous && previous.SequenceEqual(fingerprint)
            ? new TransferOutcome(await RepresentAsync(transfer, cancellationToken), Replayed: true)
            : throw new IdempotencyKeyReusedException(key);

    private async Task<TransferOutcome?> ReplayRequestAsync(Actor requester, Guid key, byte[] fingerprint, CancellationToken cancellationToken)
    {
        var existing = await db.CustodyTransfers.AsNoTracking()
            .SingleOrDefaultAsync(t => t.RequestedById == requester.UserId && t.ClientRequestId == key, cancellationToken);
        if (existing is null)
            return null;
        return existing.RequestFingerprint.SequenceEqual(fingerprint)
            ? new TransferOutcome(await RepresentAsync(existing, cancellationToken), Replayed: true)
            : throw new IdempotencyKeyReusedException(key);
    }

    private async Task<CustodyTransfer?> PendingForAsync(string evidenceCode, CancellationToken cancellationToken)
    {
        var evidenceId = await db.Evidence.Where(e => e.Code == evidenceCode).Select(e => e.EvidenceId).SingleAsync(cancellationToken);
        return await db.CustodyTransfers.AsNoTracking()
            .SingleOrDefaultAsync(t => t.EvidenceId == evidenceId && t.Status == TransferStatus.Pending, cancellationToken);
    }

    private Task<EvidenceInboxRow> InboxRowAsync(Evidence evidence, CancellationToken cancellationToken) =>
        db.EvidenceInbox.SingleAsync(r => r.EvidenceId == evidence.EvidenceId, cancellationToken);

    /// <summary>The A4 projection follows the write in the same transaction.</summary>
    private static void Project(EvidenceInboxRow inbox, Evidence evidence, CustodyTransfer transfer, DateTime occurredAtUtc, PeopleIndex people)
    {
        inbox.EventCount = evidence.EventCount;
        inbox.LastEventAtUtc = occurredAtUtc;
        inbox.CurrentCustodianId = evidence.CurrentCustodianId;
        inbox.CurrentCustodianName = people.Of(evidence.CurrentCustodianId).DisplayName;
        var pending = transfer.Status == TransferStatus.Pending;
        inbox.PendingTransferId = pending ? transfer.TransferId : null;
        inbox.PendingToCustodianId = pending ? transfer.ToCustodianId : null;
        inbox.PendingSinceUtc = pending ? transfer.RequestedAtUtc : null;
    }

    private async Task<TransferResource> RepresentAsync(CustodyTransfer transfer, CancellationToken cancellationToken)
    {
        var code = await db.Evidence.Where(e => e.EvidenceId == transfer.EvidenceId).Select(e => e.Code).SingleAsync(cancellationToken);
        var people = await PeopleIndex.LoadAsync(db, cancellationToken);
        return new TransferResource(
            transfer.TransferId, code, transfer.Status, people.Of(transfer.FromCustodianId), people.Of(transfer.ToCustodianId),
            people.Of(transfer.RequestedById), transfer.RequestedAtUtc, transfer.Reason,
            transfer.DecidedById is { } decidedBy ? people.Of(decidedBy) : null, transfer.DecidedAtUtc, transfer.DecisionNotes,
            EntityTags.Format(transfer.RowVersion));
    }
}
