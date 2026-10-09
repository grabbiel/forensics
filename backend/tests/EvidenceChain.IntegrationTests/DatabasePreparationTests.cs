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
    public async Task App_login_reads_and_records_verdicts_but_changes_nothing_else()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? "");
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var connection = await OpenAsAppAsync(cancellationToken);
        await using var read = new SqlCommand("SELECT COUNT(*) FROM dbo.Evidence;", connection);
        await using var verdict = new SqlCommand("UPDATE dbo.EvidenceInbox SET IntegrityStatus = IntegrityStatus, IntegrityCheckedAtUtc = IntegrityCheckedAtUtc, IntegrityCheckedThroughSeq = IntegrityCheckedThroughSeq;", connection);
        await using var description = new SqlCommand("UPDATE dbo.EvidenceInbox SET Description = Description;", connection);
        await using var delete = new SqlCommand("DELETE FROM dbo.Evidence;", connection);

        Assert.True((int)(await read.ExecuteScalarAsync(cancellationToken))! > 0);
        await verdict.ExecuteNonQueryAsync(cancellationToken);
        Assert.Equal(230, (await Assert.ThrowsAsync<SqlException>(() => description.ExecuteNonQueryAsync(cancellationToken))).Number); // denied on the column
        Assert.Equal(229, (await Assert.ThrowsAsync<SqlException>(() => delete.ExecuteNonQueryAsync(cancellationToken))).Number); // denied on the object
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

        Assert.Equal("DENY DELETE, DENY UPDATE", await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task<SqlConnection> OpenAsAppAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(factory.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
