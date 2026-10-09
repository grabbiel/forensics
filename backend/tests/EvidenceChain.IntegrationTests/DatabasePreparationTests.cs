using Microsoft.Data.SqlClient;

namespace EvidenceChain.IntegrationTests;

[Collection(nameof(SqlCollection))]
public sealed class DatabasePreparationTests(ApiFactory factory)
{
    [Fact]
    public async Task Prepare_reruns_cleanly_and_leaves_RCSI_and_snapshot_isolation_on()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? "");
        var cancellationToken = TestContext.Current.CancellationToken;

        await factory.PrepareAsync(cancellationToken);

        await using var connection = await OpenAsAppAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT CONCAT(is_read_committed_snapshot_on, '/', snapshot_isolation_state_desc) FROM sys.databases WHERE name = DB_NAME();", connection);
        Assert.Equal("1/ON", await command.ExecuteScalarAsync(cancellationToken));
    }

    [Fact]
    public async Task App_login_cannot_change_the_schema()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? "");
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var connection = await OpenAsAppAsync(cancellationToken);
        await using var command = new SqlCommand("CREATE TABLE dbo.ShouldNotExist (Id int);", connection);

        var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync(cancellationToken));
        Assert.Equal(262, error.Number); // permission denied in database
    }

    [Fact]
    public async Task App_login_writes_only_the_columns_custody_writes_need()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? "");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = await OpenAsAppAsync(cancellationToken);

        await using (var read = new SqlCommand("SELECT COUNT(*) FROM dbo.Evidence;", connection))
            Assert.True((int)(await read.ExecuteScalarAsync(cancellationToken))! > 0);

        // Allowed: what requests, decisions and verification write (no-op updates, so nothing changes).
        foreach (var allowed in new[]
        {
            "UPDATE dbo.Evidence SET HeadMac = HeadMac, EventCount = EventCount, CurrentCustodianId = CurrentCustodianId;",
            "UPDATE dbo.CustodyTransfers SET Status = Status, DecidedAtUtc = DecidedAtUtc, DecisionNotes = DecisionNotes;",
            "UPDATE dbo.EvidenceInbox SET EventCount = EventCount, PendingTransferId = PendingTransferId, IntegrityStatus = IntegrityStatus;",
        })
        {
            await using var command = new SqlCommand(allowed, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Denied: anything else, by column (230) or by object (229).
        foreach (var (denied, error) in new[]
        {
            ("UPDATE dbo.Evidence SET Description = Description;", 230),
            ("UPDATE dbo.Evidence SET InitialCustodianId = InitialCustodianId;", 230),
            ("UPDATE dbo.CustodyTransfers SET Reason = Reason;", 230),
            ("UPDATE dbo.EvidenceInbox SET Description = Description;", 230),
            ("DELETE FROM dbo.Evidence;", 229),
            ("DELETE FROM dbo.CustodyTransfers;", 229),
            ("INSERT dbo.Users (UserId, UserName, DisplayName, Email, Role) VALUES (99, 'x', 'x', 'x@example.test', 'Supervisor');", 229),
        })
        {
            await using var command = new SqlCommand(denied, connection);
            Assert.Equal(error, (await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync(cancellationToken))).Number);
        }
    }

    [Fact]
    public async Task App_login_is_denied_rewriting_custody_history()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? "");
        var cancellationToken = TestContext.Current.CancellationToken;

        // An explicit DENY, not just a missing grant, so a future write grant cannot reopen it.
        await using var admin = new SqlConnection(factory.AdminConnectionString);
        await admin.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT STRING_AGG(p.state_desc + ' ' + p.permission_name, ', ') WITHIN GROUP (ORDER BY p.permission_name)
            FROM sys.database_permissions AS p
            WHERE p.major_id = OBJECT_ID('dbo.CustodyEvents') AND p.grantee_principal_id = DATABASE_PRINCIPAL_ID('evidence_app')
            """, admin);

        Assert.Equal("DENY DELETE, GRANT INSERT, DENY UPDATE", await command.ExecuteScalarAsync(cancellationToken)); // appended, never changed
    }

    private async Task<SqlConnection> OpenAsAppAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(factory.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
