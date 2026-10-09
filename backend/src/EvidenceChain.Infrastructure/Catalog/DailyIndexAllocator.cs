using System.Data;
using EvidenceChain.Domain.Catalog;
using EvidenceChain.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EvidenceChain.Infrastructure.Catalog;

/// <summary>Raised when a (type, day) has used all 9,999 daily indexes; maps to the 409 problem daily-index-exhausted.</summary>
public sealed class DailyIndexExhaustedException(string typeCode, DateOnly day)
    : Exception($"All {EvidenceCode.MaxDailyNo} {typeCode} codes for {day:yyyy-MM-dd} are taken.")
{
    public string TypeCode { get; } = typeCode;

    public DateOnly Day { get; } = day;
}

/// <summary>Hands out the next daily index for (type, UTC day), gap-free under concurrency.</summary>
public static class DailyIndexAllocator
{
    // UPDLOCK + HOLDLOCK take a key-range lock even when the row is missing, so two first-of-the-day
    // registrations queue instead of both inserting. Returns the index, or 0 when the day is exhausted.
    private const string Sql = """
        DECLARE @n smallint;
        UPDATE dbo.DailyCounter WITH (UPDLOCK, HOLDLOCK)
           SET @n = LastNo = LastNo + 1
         WHERE TypeCode = @type AND [Day] = @day AND LastNo < @max;
        IF @@ROWCOUNT = 1
            SELECT @n;
        ELSE IF EXISTS (SELECT 1 FROM dbo.DailyCounter WITH (UPDLOCK, HOLDLOCK) WHERE TypeCode = @type AND [Day] = @day)
            SELECT CAST(0 AS smallint);
        ELSE
        BEGIN
            INSERT dbo.DailyCounter (TypeCode, [Day], LastNo) VALUES (@type, @day, 1);
            SELECT CAST(1 AS smallint);
        END;
        """;

    /// <summary>
    /// Allocates on the context's open transaction, which must also insert the evidence: the lock is held
    /// until it commits, and a rollback returns the index.
    /// </summary>
    public static async Task<short> NextAsync(AppDbContext db, string typeCode, DateOnly day, CancellationToken cancellationToken)
    {
        if (!EvidenceTypes.IsKnown(typeCode))
            throw new ArgumentOutOfRangeException(nameof(typeCode), typeCode, "Unknown evidence type.");
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Allocate inside the transaction that registers the evidence.");

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandText = Sql;
        command.Parameters.Add(new SqlParameter("@type", SqlDbType.Char, 3) { Value = typeCode });
        command.Parameters.Add(new SqlParameter("@day", SqlDbType.Date) { Value = day.ToDateTime(TimeOnly.MinValue) });
        command.Parameters.Add(new SqlParameter("@max", SqlDbType.SmallInt) { Value = (short)EvidenceCode.MaxDailyNo });

        var next = (short)(await command.ExecuteScalarAsync(cancellationToken))!;
        // Raised before any code is formatted: "D4" is only a minimum width, so 10000 would format silently.
        return next == 0 ? throw new DailyIndexExhaustedException(typeCode, day) : next;
    }
}
