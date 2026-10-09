using System.Data;
using Microsoft.EntityFrameworkCore;

namespace EvidenceChain.Infrastructure.Persistence;

internal static class SnapshotRead
{
    /// <summary>
    /// Runs several reads in one SNAPSHOT transaction, so they all see the database as of the first: a transfer committing
    /// between two statements can neither pass for tampering nor mix two versions of an evidence. Never blocks writers.
    /// </summary>
    public static Task<T> InSnapshotAsync<T>(this AppDbContext db, Func<Task<T>> reads, CancellationToken cancellationToken) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellationToken);
            var result = await reads();
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
}
