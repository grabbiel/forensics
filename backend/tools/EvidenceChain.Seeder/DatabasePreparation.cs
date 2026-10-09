using System.Data;
using Microsoft.Data.SqlClient;

namespace EvidenceChain.Seeder;

/// <summary>Local-only database setup that EF migrations cannot do (no transactions, server-level objects).</summary>
internal static class DatabasePreparation
{
    /// <summary>Turns on read-committed snapshot; a no-op when already on (Azure SQL default).</summary>
    public static async Task EnableReadCommittedSnapshotAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()) = 0
                ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
            """;
        await ExecuteAsync(connection, sql, [], cancellationToken);
    }

    /// <summary>
    /// Allows SNAPSHOT transactions, which the API's multi-statement reads use so a write committing between two
    /// statements cannot make a sound chain look broken; a no-op when already on (Azure SQL default).
    /// </summary>
    public static async Task EnableSnapshotIsolationAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            IF (SELECT snapshot_isolation_state FROM sys.databases WHERE name = DB_NAME()) = 0
                ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;
            """;
        await ExecuteAsync(connection, sql, [], cancellationToken);
    }

    /// <summary>Creates (or re-passwords) the API's least-privilege SQL login and user (local Docker only).</summary>
    public static async Task CreateAppLoginAsync(SqlConnection connection, string login, string password, CancellationToken cancellationToken)
    {
        // QUOTENAME returns NULL past 128 characters, and the parameters would truncate silently.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(login.Length, 128);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(password.Length, 128);

        // CREATE LOGIN/USER take no parameters and EXEC() can't call functions: quote into variables, then sp_executesql.
        const string sql = """
            DECLARE @quotedLogin nvarchar(258) = QUOTENAME(@login);
            DECLARE @quotedPassword nvarchar(258) = QUOTENAME(@password, N'''');

            DECLARE @sql nvarchar(max) = CASE WHEN SUSER_ID(@login) IS NULL
                THEN N'CREATE LOGIN ' + @quotedLogin + N' WITH PASSWORD = ' + @quotedPassword
                ELSE N'ALTER LOGIN ' + @quotedLogin + N' WITH PASSWORD = ' + @quotedPassword END;
            EXEC sys.sp_executesql @sql;

            IF USER_ID(@login) IS NULL
            BEGIN
                SET @sql = N'CREATE USER ' + @quotedLogin + N' FOR LOGIN ' + @quotedLogin;
                EXEC sys.sp_executesql @sql;
            END;

            SET @sql = N'ALTER ROLE db_datareader ADD MEMBER ' + @quotedLogin;
            EXEC sys.sp_executesql @sql;

            -- Exactly the writes custody needs, by column: requests insert a transfer and decisions fill in its decision;
            -- events are appended; the evidence's head and holder follow them; the inbox projection follows every write
            -- and records verification verdicts.
            SET @sql = REPLACE(N'
                GRANT INSERT ON dbo.CustodyTransfers TO {app};
                GRANT UPDATE ON dbo.CustodyTransfers (Status, DecidedAtUtc, DecidedById, DecisionNotes, DecisionKey, DecisionFingerprint) TO {app};
                GRANT INSERT ON dbo.CustodyEvents TO {app};
                GRANT UPDATE ON dbo.Evidence (HeadMac, EventCount, CurrentCustodianId) TO {app};
                GRANT UPDATE ON dbo.EvidenceInbox (EventCount, LastEventAtUtc, CurrentCustodianId, CurrentCustodianName,
                    PendingTransferId, PendingToCustodianId, PendingSinceUtc,
                    IntegrityStatus, IntegrityCheckedAtUtc, IntegrityCheckedThroughSeq) TO {app};', N'{app}', @quotedLogin);
            EXEC sys.sp_executesql @sql;

            -- Explicit DENY outlives any later write grant: the app can never rewrite custody history.
            SET @sql = N'DENY UPDATE, DELETE ON dbo.CustodyEvents TO ' + @quotedLogin;
            EXEC sys.sp_executesql @sql;
            """;
        // Column-level grants; CustodyEvents keeps its DENY, and nothing may delete.
        await ExecuteAsync(connection, sql,
        [
            new SqlParameter("@login", SqlDbType.NVarChar, 128) { Value = login },
            new SqlParameter("@password", SqlDbType.NVarChar, 128) { Value = password },
        ], cancellationToken);
    }

    /// <summary>Runs one parameterized batch.</summary>
    private static async Task ExecuteAsync(SqlConnection connection, string sql, SqlParameter[] parameters, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
