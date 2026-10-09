# AI-REVIEW-01

The block accepts every pending transfer at once and searches transfers by custodian name. Findings, most severe first:

| # | Defect | Severity | Impact | Fix |
|---|---|---|---|---|
| 1 | SQL injection: `FromSqlRaw` with an interpolated string (`'{name}'`) | Critical | `name = x' OR '1'='1` returns every transfer, and stacked statements can change data, with the API's database permissions. | `FromSql($"… = {name}")` or LINQ, both of which send `name` as a parameter. |
| 2 | No authorization: it accepts every pending transfer, for every recipient | Critical | Whoever can call it takes decisions that belong to other custodians; the brief requires the recipient check on the server. | Accept one transfer, for the authenticated recipient, behind a role policy and a recipient check. |
| 3 | Parallel `SaveChangesAsync` on one `DbContext` | Critical | `DbContext` is not thread-safe: it throws "a second operation was started on this context" or corrupts change tracking. Each save writes every tracked change and commits on its own, so a failure part-way leaves some transfers accepted and others not. | Change the entities, then call `SaveChangesAsync` once, in one transaction. |
| 4 | `async void` | High | The caller cannot await it or catch its exceptions: the request can end and dispose the context mid-operation, and an unhandled exception terminates the process. | `async Task`, with a `CancellationToken`. |
| 5 | Bypasses the state machine and the custody chain | High | Setting `Status` directly skips the state machine and the recipient check. Custody is not handed over, no signed event is appended and the inbox is not updated, so chain verification will flag the evidence. | Call the domain's `Accept`, which checks the state and the recipient and hands over custody. Append the signed event and update the projection in the same transaction. |
| 6 | No optimistic concurrency at the API | High | Nothing compares the version the client saw. Without a concurrency token, a reject committed in between is overwritten (lost update). With one, the exception escapes the `async void` instead of becoming a `409`. | Require `If-Match` and compare it with `rowversion`; map `DbUpdateConcurrencyException` to `409` with the current state. |
| 7 | `DateTime.Now` stored in `AcceptedAtUtc` | Medium | Server-local time labelled UTC: wrong by the offset, ambiguous at daylight-saving changes. It breaks deadlines and signed timestamps, and tests cannot control it. | One `TimeProvider.GetUtcNow()` per operation. |
| 8 | Unbounded queries | Medium | `ToListAsync` loads every pending transfer, and `Search` loads every match with `SELECT *`. | A limit and a stable order; project only the needed columns; `AsNoTracking` for reads. |
| 9 | No idempotency | Medium | A retried call repeats the work and cannot return the first answer. | An `Idempotency-Key`, stored with the change. |
| 10 | Synchronous `ToList()`, no cancellation, tracked entities returned | Low | It blocks a thread per call, pays for change tracking and exposes the entity shape. | `ToListAsync(cancellationToken)` with a projection to a DTO. |

Against this repository's model, the block would not even compile: `AcceptedAtUtc` does not exist, and `Status` has a private setter, so every change goes through `CustodyTransfer.Accept`.

**Corrected.** This sketch follows the project's `CustodyTransferService.DecideAsync`.

```csharp
public async Task<TransferResource> AcceptAsync(
    long transferId, Actor recipient, byte[] ifMatch, Guid idempotencyKey, byte[] fingerprint, CancellationToken ct) =>
    await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var evidence = await LockEvidenceOfAsync(transferId, ct);         // UPDLOCK on its evidence: writes queue here
        var transfer = await _db.CustodyTransfers.SingleAsync(t => t.TransferId == transferId, ct);
        if (transfer.DecisionKey == idempotencyKey)
            return Replay(transfer, fingerprint);                         // same key: same answer, or 422 for another body
        if (!transfer.RowVersion.SequenceEqual(ifMatch))
            throw new StaleVersionException(transfer);                    // 409 with the current state
        var now = _clock.GetUtcNow().UtcDateTime;
        transfer.Accept(evidence, recipient, now, notes: null, idempotencyKey, fingerprint); // state and recipient checks
        _db.CustodyEvents.Add(CustodyLedger.Append(_keys, evidence, transfer, CustodyEventKind.TransferAccepted, recipient.UserId, now));
        await ProjectInboxAsync(evidence, transfer, now, ct);           // the inbox row, in the same transaction
        await _db.SaveChangesAsync(ct);                                   // one save; rowversion checked again
        await transaction.CommitAsync(ct);
        return Represent(transfer);
    });

// By id: display names are not unique.
public Task<List<TransferSummary>> SearchByCustodianAsync(int custodianId, int limit, CancellationToken ct) =>
    _db.CustodyTransfers.AsNoTracking()
        .Where(t => t.ToCustodianId == custodianId)
        .OrderByDescending(t => t.RequestedAtUtc).ThenByDescending(t => t.TransferId)
        .Take(limit)
        .Select(t => new TransferSummary(t.TransferId, t.EvidenceId, t.Status, t.RequestedAtUtc))
        .ToListAsync(ct);
```

Bulk acceptance is dropped on purpose: each acceptance is the recipient's own decision.
