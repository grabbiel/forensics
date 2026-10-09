using System.Data;
using EvidenceChain.Domain.Integrity;
using EvidenceChain.SyntheticData;
using Microsoft.Data.SqlClient;

namespace EvidenceChain.Seeder;

/// <summary>How seed treats a database that already holds data.</summary>
internal enum SeedMode
{
    /// <summary>Only an empty database is seeded.</summary>
    Strict,

    /// <summary>A database holding exactly this seed is left alone; any other data is refused.</summary>
    IfEmpty,

    /// <summary>Existing data is replaced.</summary>
    Reset,
}

internal sealed record SeedResult(bool AlreadySeeded, int Users, int Evidences, int Transfers, int Events);

/// <summary>Loads a synthetic dataset into the custody schema, tamper fixtures included, all or nothing.</summary>
internal static class DatasetLoader
{
    private const string AppendOnlyTrigger = "TR_CustodyEvents_AppendOnly";

    // Every table the seed writes; "empty" means all of them.
    private static readonly string[] SeededTables =
        ["Users", "Evidence", "EvidenceContent", "DailyCounter", "CustodyTransfers", "CustodyEvents", "EvidenceInbox", "SeedRuns"];

    /// <summary>
    /// One transaction, so a failure leaves the database exactly as it was: decide, optionally reset, load,
    /// check codes, apply the tamper fixtures, mark the run complete. Needs an administrator (it toggles a trigger).
    /// </summary>
    public static async Task<SeedResult> SeedAsync(
        string connectionString, SyntheticDataset dataset, IntegrityKeyRing keys, SeedMode mode, string arguments, CancellationToken cancellationToken)
    {
        // Built before touching the database, so a generation error changes nothing.
        var rows = SeedRows.Build(dataset, keys);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteAsync(connection, transaction, "EXEC sys.sp_getapplock @Resource = N'EvidenceChain.Seed', @LockMode = 'Exclusive', @LockOwner = 'Transaction';", cancellationToken);

            if (mode == SeedMode.IfEmpty && await HoldsThisSeedAsync(connection, transaction, dataset, cancellationToken))
                return new SeedResult(AlreadySeeded: true, 0, 0, 0, 0);

            if (await HasDataAsync(connection, transaction, cancellationToken))
            {
                if (mode != SeedMode.Reset)
                    throw new ArgumentException("The database already holds other data; pass --reset to replace it.");
                await ResetAsync(connection, transaction, cancellationToken);
            }

            var runId = await StartRunAsync(connection, transaction, dataset, arguments, cancellationToken);
            await BulkLoadAsync(connection, transaction, rows, cancellationToken);
            await AssertCodesAsync(connection, transaction, rows, cancellationToken);
            await ApplyTamperFixturesAsync(connection, transaction, dataset, rows, cancellationToken);
            await ExecuteAsync(connection, transaction, "UPDATE dbo.SeedRuns SET CompletedAtUtc = SYSUTCDATETIME() WHERE SeedRunId = @id", cancellationToken, ("@id", runId));
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            // A rollback restores the trigger too; this is the belt to that brace.
            await ExecuteAsync(connection, null, $"ENABLE TRIGGER dbo.{AppendOnlyTrigger} ON dbo.CustodyEvents;", CancellationToken.None);
        }

        if (await ScalarAsync<bool>(connection, null, $"SELECT is_disabled FROM sys.triggers WHERE name = '{AppendOnlyTrigger}'", cancellationToken))
            throw new InvalidOperationException($"{AppendOnlyTrigger} is still disabled.");

        return new SeedResult(false, rows.Users.Rows.Count, rows.Evidence.Rows.Count, rows.CustodyTransfers.Rows.Count, rows.CustodyEvents.Rows.Count);
    }

    /// <summary>
    /// True only for a completed run of the same seed and sizes whose evidence and events are all still there, checked by
    /// their ids (the load keeps ids 1 to N). Rows written afterwards are fine: the API appends events and the app is
    /// meant to be used between restarts. A missing seeded row means a damaged load, whatever was added since.
    /// The anchor is left out on purpose: it defaults to today, so a restart on a later day is still the same seed.
    /// </summary>
    private static async Task<bool> HoldsThisSeedAsync(SqlConnection connection, SqlTransaction transaction, SyntheticDataset dataset, CancellationToken cancellationToken)
    {
        var runs = await ScalarAsync<int>(connection, transaction,
            "SELECT COUNT(*) FROM dbo.SeedRuns WHERE Seed = @seed AND Profile = @spec AND CompletedAtUtc IS NOT NULL", cancellationToken,
            ("@seed", (long)dataset.Seed), ("@spec", dataset.Profile.Spec));
        if (runs == 0)
            return false;

        var (evidences, events) = (
            await ScalarAsync<int>(connection, transaction, "SELECT COUNT(*) FROM dbo.Evidence WHERE EvidenceId BETWEEN 1 AND @n", cancellationToken,
                ("@n", (long)dataset.Evidences.Count)),
            await ScalarAsync<int>(connection, transaction, "SELECT COUNT(*) FROM dbo.CustodyEvents WHERE CustodyEventId BETWEEN 1 AND @n", cancellationToken,
                ("@n", (long)dataset.Events.Count)));
        if ((evidences, events) != (dataset.Evidences.Count, dataset.Events.Count))
            throw new ArgumentException(
                $"Seed {dataset.Seed} was loaded here, but {dataset.Evidences.Count - evidences} of its evidences and {dataset.Events.Count - events} of its events are gone; pass --reset to reload it.");
        return true;
    }

    private static async Task<bool> HasDataAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken) =>
        await ScalarAsync<int>(connection, transaction,
            $"SELECT CASE WHEN {string.Join(" OR ", SeededTables.Select(t => $"EXISTS (SELECT 1 FROM dbo.{t})"))} THEN 1 ELSE 0 END", cancellationToken) == 1;

    /// <summary>Deletes every seeded row; the append-only trigger is lifted only for the event delete.</summary>
    private static Task ResetAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken) =>
        ExecuteAsync(connection, transaction, $"""
            DISABLE TRIGGER dbo.{AppendOnlyTrigger} ON dbo.CustodyEvents;
            DELETE dbo.CustodyEvents;
            ENABLE TRIGGER dbo.{AppendOnlyTrigger} ON dbo.CustodyEvents;
            DELETE dbo.EvidenceInbox;
            DELETE dbo.CustodyTransfers;
            DELETE dbo.EvidenceContent;
            DELETE dbo.Evidence;
            DELETE dbo.DailyCounter;
            DELETE dbo.SeedRuns;
            DELETE dbo.Users;
            """, cancellationToken);

    private static Task<int> StartRunAsync(SqlConnection connection, SqlTransaction transaction, SyntheticDataset dataset, string arguments, CancellationToken cancellationToken) =>
        ScalarAsync<int>(connection, transaction, """
            INSERT dbo.SeedRuns (Seed, AnchorUtc, Profile, Evidences, Events, StartedAtUtc, Arguments)
            OUTPUT INSERTED.SeedRunId
            VALUES (@seed, @anchor, @spec, @evidences, @events, SYSUTCDATETIME(), @arguments)
            """, cancellationToken, ("@seed", (long)dataset.Seed), ("@anchor", dataset.AnchorUtc), ("@spec", dataset.Profile.Spec),
            ("@evidences", dataset.Evidences.Count), ("@events", dataset.Events.Count), ("@arguments", arguments));

    /// <summary>Parents before children; ids come from the rows, and constraints are checked so they stay trusted.</summary>
    private static async Task BulkLoadAsync(SqlConnection connection, SqlTransaction transaction, SeedRows rows, CancellationToken cancellationToken)
    {
        foreach (var (table, data) in new[]
        {
            ("Users", rows.Users), ("Evidence", rows.Evidence), ("EvidenceContent", rows.EvidenceContent), ("CustodyTransfers", rows.CustodyTransfers),
            ("CustodyEvents", rows.CustodyEvents), ("DailyCounter", rows.DailyCounter), ("EvidenceInbox", rows.EvidenceInbox),
        })
        {
            using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.KeepNulls, transaction)
            {
                DestinationTableName = $"dbo.{table}",
                BatchSize = 5_000,
                BulkCopyTimeout = 0,
            };
            foreach (DataColumn column in data.Columns)
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            await bulk.WriteToServerAsync(data, cancellationToken);
        }
    }

    /// <summary>SQL Server computes the code; it must equal the one the generator formatted.</summary>
    private static async Task AssertCodesAsync(SqlConnection connection, SqlTransaction transaction, SeedRows rows, CancellationToken cancellationToken)
    {
        var stored = new Dictionary<long, string>();
        await using (var command = new SqlCommand("SELECT EvidenceId, Code FROM dbo.Evidence", connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                stored[reader.GetInt64(0)] = reader.GetString(1);
        }

        var mismatch = rows.EvidenceIds.FirstOrDefault(p => stored.GetValueOrDefault(p.Value) != p.Key);
        if (mismatch.Key is not null)
            throw new InvalidOperationException($"SQL computed '{stored.GetValueOrDefault(mismatch.Value)}' for {mismatch.Key}.");
    }

    /// <summary>The three documented tampers, each bypassing the application as someone with database access would.</summary>
    private static Task ApplyTamperFixturesAsync(SqlConnection connection, SqlTransaction transaction, SyntheticDataset dataset, SeedRows rows, CancellationToken cancellationToken)
    {
        var f = dataset.Fixtures;
        var tamperedContent = dataset.Evidences.Single(e => e.Code == f.ContentTampered).Content.ToArray(); // never mutate the dataset
        tamperedContent[f.ContentTamperedOffset] ^= 0x20; // flips an ASCII letter's case: same length, still UTF-8

        return ExecuteAsync(connection, transaction, $"""
            DISABLE TRIGGER dbo.{AppendOnlyTrigger} ON dbo.CustodyEvents;
            UPDATE dbo.CustodyEvents SET Notes = Notes + N' [editado]' WHERE EvidenceId = @eventEvidence AND Seq = @seq;
            ENABLE TRIGGER dbo.{AppendOnlyTrigger} ON dbo.CustodyEvents;
            UPDATE dbo.EvidenceContent SET Bytes = @bytes WHERE EvidenceId = @contentEvidence;
            UPDATE dbo.Evidence SET CurrentCustodianId = @custodian WHERE EvidenceId = @custodianEvidence;
            """, cancellationToken,
            ("@eventEvidence", rows.EvidenceIds[f.EventTampered]), ("@seq", f.EventTamperedSeq),
            ("@bytes", tamperedContent), ("@contentEvidence", rows.EvidenceIds[f.ContentTampered]),
            ("@custodian", dataset.Users.Single(u => u.UserName == f.CustodianTamperedTo).Id), ("@custodianEvidence", rows.EvidenceIds[f.CustodianTampered]));
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction? transaction, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<T> ScalarAsync<T>(SqlConnection connection, SqlTransaction? transaction, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static SqlCommand Command(SqlConnection connection, SqlTransaction? transaction, string sql, (string Name, object Value)[] parameters)
    {
        var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 300 };
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return command;
    }
}
