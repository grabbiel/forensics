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
/// Every write first takes an update lock on its evidence's row, so writes to one evidence run one after another and
/// everything read after the lock is the latest committed state. Clients still get optimistic concurrency: If-Match
/// decides the 409, and no lock is held between a client's read and its write. A retry of a write that already
/// committed answers without the lock, and every answer is built after the transaction: nothing waits on the lock for
/// it, and a fault while building it cannot run the write again.
/// </summary>
internal sealed class CustodyTransferService(AppDbContext db, IntegrityKeyRing keys, TimeProvider clock, PeopleIndexProvider peopleIndex) : ICustodyTransfers
{
    private const string RequestKeyIndex = "UX_CustodyTransfers_RequestKey";
    private const string OnePendingIndex = "UX_CustodyTransfers_OnePendingPerEvidence";
    private const string DecisionKeyIndex = "UX_CustodyTransfers_DecisionKey";
    private const string EventSeqIndex = "UX_CustodyEvents_EvidenceSeq";
    private const int RequestAttempts = 3;

    /// <summary>What a write left: the transfer, the evidence code when it is at hand, and whether it answered an earlier request.</summary>
    private sealed record Written(CustodyTransfer Transfer, string? EvidenceCode, bool Replayed);

    public async Task<TransferResource?> GetAsync(long transferId, CancellationToken cancellationToken)
    {
        var transfer = await db.CustodyTransfers.AsNoTracking().SingleOrDefaultAsync(t => t.TransferId == transferId, cancellationToken);
        return transfer is null ? null : await RepresentAsync(transfer, code:null, cancellationToken);
    }

    public async Task<TransferOutcome> RequestAsync(TransferRequest request, Actor requester, Guid idempotencyKey, byte[] fingerprint, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var written = await db.Database.CreateExecutionStrategy().ExecuteAsync(
                    () => RequestOnceAsync(request, requester, idempotencyKey, fingerprint, cancellationToken));
                return await OutcomeAsync(written, cancellationToken);
            }
            catch (DbUpdateException e) when (UniqueViolation.IndexOf(e) is RequestKeyIndex or OnePendingIndex)
            {
                // Another request committed first. A duplicate of ours breaks both indexes and SQL Server names either,
                // so look for our key before calling it a second pending transfer.
                db.ChangeTracker.Clear();
                if (await FindRequestAsync(requester, idempotencyKey, fingerprint, cancellationToken) is { } replay)
                    return await OutcomeAsync(new Written(replay, request.EvidenceCode, Replayed: true), cancellationToken);
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
            var written = await db.Database.CreateExecutionStrategy().ExecuteAsync(
                () => DecideOnceAsync(transferId, command, notes, decider, expectedVersion, idempotencyKey, fingerprint, cancellationToken));
            return await OutcomeAsync(written, cancellationToken);
        }
        catch (DbUpdateException e) when (e is DbUpdateConcurrencyException || UniqueViolation.IndexOf(e) == EventSeqIndex)
        {
            // Someone decided between our read and our save: perhaps our own retry, sent in parallel; otherwise report the
            // transfer as it is now.
            db.ChangeTracker.Clear();
            var current = await db.CustodyTransfers.AsNoTracking().SingleAsync(t => t.TransferId == transferId, cancellationToken);
            if (current.DecisionKey == idempotencyKey)
                return await OutcomeAsync(
                    new Written(SameDecision(current, decider, idempotencyKey, fingerprint), EvidenceCode: null, Replayed: true), cancellationToken);
            throw current.Status == TransferStatus.Pending ? new StaleVersionException(current) : new InvalidTransitionException(command, current);
        }
        catch (DbUpdateException e) when (UniqueViolation.IndexOf(e) == DecisionKeyIndex)
        {
            throw new IdempotencyKeyReusedException(idempotencyKey); // already used by this decider on another transfer
        }
    }

    private async Task<Written> RequestOnceAsync(TransferRequest request, Actor requester, Guid key, byte[] fingerprint, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear(); // a retry starts from what is committed
        // A retry of a request that already committed answers from it, without waiting for the evidence's lock: reads see
        // only committed rows, so this never blocks and never finds a request still in flight. The same fingerprint means
        // the same request, so its evidence code is the request's.
        if (await FindRequestAsync(requester, key, fingerprint, cancellationToken) is { } committed)
            return new Written(committed, request.EvidenceCode, Replayed: true);
        var evidenceId = await db.Evidence.Where(e => e.Code == request.EvidenceCode).Select(e => (long?)e.EvidenceId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidRequestException("evidenceCode", "No evidence has that code.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var evidence = await LockEvidenceAsync(evidenceId, cancellationToken);
        // After the lock: a duplicate sent in parallel waits here and then finds the first one's transfer.
        if (await FindRequestAsync(requester, key, fingerprint, cancellationToken) is { } replay)
            return new Written(replay, evidence.Code, Replayed: true);
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
        var people = await peopleIndex.GetAsync(cancellationToken);
        Project(await InboxRowAsync(evidence, cancellationToken), evidence, transfer, now, people);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new Written(transfer, evidence.Code, Replayed: false);
    }

    private async Task<Written> DecideOnceAsync(
        long transferId, TransferCommand command, string? notes, Actor decider, byte[] expectedVersion, Guid key, byte[] fingerprint,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var stored = await db.CustodyTransfers.AsNoTracking().SingleOrDefaultAsync(t => t.TransferId == transferId, cancellationToken)
            ?? throw new InvalidOperationException($"Transfer {transferId} does not exist.");
        // A retry of a decision that already committed answers from it, without waiting for the evidence's lock.
        if (stored.DecisionKey == key)
            return new Written(SameDecision(stored, decider, key, fingerprint), EvidenceCode: null, Replayed: true);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // The evidence first, as requests do; then the transfer, which no other write can be changing now.
        var evidence = await LockEvidenceAsync(stored.EvidenceId, cancellationToken);
        var transfer = await db.CustodyTransfers.SingleAsync(t => t.TransferId == transferId, cancellationToken);

        // A retry of a decision made while this one waited for the lock: answer as it did, whatever version the retry names.
        if (transfer.DecisionKey == key)
            return new Written(SameDecision(transfer, decider, key, fingerprint), evidence.Code, Replayed: true);

        // A pending transfer only changes version when decided, so a different If-Match is an outdated read.
        if (transfer.Status == TransferStatus.Pending && !transfer.RowVersion.SequenceEqual(expectedVersion))
            throw new StaleVersionException(transfer);
        // And the save only matches the row at the version the client read.
        db.Entry(transfer).Property(t => t.RowVersion).OriginalValue = expectedVersion;

        var now = clock.GetUtcNow().UtcDateTime;
        if (command == TransferCommand.Accept)
            transfer.Accept(evidence, decider, now, notes, key, fingerprint);
        else
            transfer.Reject(evidence, decider, now, notes ?? string.Empty, key, fingerprint);

        var kind = command == TransferCommand.Accept ? CustodyEventKind.TransferAccepted : CustodyEventKind.TransferRejected;
        db.CustodyEvents.Add(CustodyLedger.Append(keys, evidence, transfer, kind, decider.UserId, now));
        var people = await peopleIndex.GetAsync(cancellationToken);
        Project(await InboxRowAsync(evidence, cancellationToken), evidence, transfer, now, people);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new Written(transfer, evidence.Code, Replayed: false);
    }

    /// <summary>A retry of a decision: the same decider with the same request; otherwise the key was reused for another.</summary>
    private static CustodyTransfer SameDecision(CustodyTransfer transfer, Actor decider, Guid key, byte[] fingerprint) =>
        transfer.DecidedById == decider.UserId && transfer.DecisionFingerprint is { } previous && previous.SequenceEqual(fingerprint)
            ? transfer
            : throw new IdempotencyKeyReusedException(key);

    /// <summary>The transfer this requester's key already created, or null; the same key with another request is a 422.</summary>
    private async Task<CustodyTransfer?> FindRequestAsync(Actor requester, Guid key, byte[] fingerprint, CancellationToken cancellationToken)
    {
        var existing = await db.CustodyTransfers.AsNoTracking()
            .SingleOrDefaultAsync(t => t.RequestedById == requester.UserId && t.ClientRequestId == key, cancellationToken);
        if (existing is null)
            return null;
        return existing.RequestFingerprint.SequenceEqual(fingerprint) ? existing : throw new IdempotencyKeyReusedException(key);
    }

    /// <summary>The answer to a write, built once its transaction is over.</summary>
    private async Task<TransferOutcome> OutcomeAsync(Written written, CancellationToken cancellationToken) =>
        new(await RepresentAsync(written.Transfer, written.EvidenceCode, cancellationToken), written.Replayed);

    private async Task<CustodyTransfer?> PendingForAsync(string evidenceCode, CancellationToken cancellationToken)
    {
        var evidenceId = await db.Evidence.Where(e => e.Code == evidenceCode).Select(e => e.EvidenceId).SingleAsync(cancellationToken);
        return await db.CustodyTransfers.AsNoTracking()
            .SingleOrDefaultAsync(t => t.EvidenceId == evidenceId && t.Status == TransferStatus.Pending, cancellationToken);
    }

    /// <summary>
    /// The evidence row, tracked, under an update lock held to the end of the transaction: a second write to the same
    /// evidence waits here until the first commits. Plain reads are not blocked (they read row versions). By primary
    /// key, so the lock is one row.
    /// </summary>
    private Task<Evidence> LockEvidenceAsync(long evidenceId, CancellationToken cancellationToken) =>
        db.Evidence.FromSql($"SELECT * FROM dbo.Evidence WITH (UPDLOCK, ROWLOCK) WHERE EvidenceId = {evidenceId}").SingleAsync(cancellationToken);

    private Task<EvidenceInboxRow> InboxRowAsync(Evidence evidence, CancellationToken cancellationToken) =>
        db.EvidenceInbox.SingleAsync(r => r.EvidenceId == evidence.EvidenceId, cancellationToken);

    /// <summary>The EvidenceInbox projection follows the write in the same transaction.</summary>
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

    private async Task<TransferResource> RepresentAsync(CustodyTransfer transfer, string? code, CancellationToken cancellationToken)
    {
        code ??= await db.Evidence.Where(e => e.EvidenceId == transfer.EvidenceId).Select(e => e.Code).SingleAsync(cancellationToken);
        var people = await peopleIndex.GetAsync(cancellationToken);
        return new TransferResource(
            transfer.TransferId, code, transfer.Status, people.Of(transfer.FromCustodianId), people.Of(transfer.ToCustodianId),
            people.Of(transfer.RequestedById), transfer.RequestedAtUtc, transfer.Reason,
            transfer.DecidedById is { } decidedBy ? people.Of(decidedBy) : null, transfer.DecidedAtUtc, transfer.DecisionNotes,
            EntityTags.Format(transfer.RowVersion));
    }

    public Task<CustodyTransfer?> FindAsync(long transferId, CancellationToken cancellationToken) => 
        db.CustodyTransfers.AsNoTracking().SingleOrDefaultAsync(t => t.TransferId == transferId, cancellationToken);
}
