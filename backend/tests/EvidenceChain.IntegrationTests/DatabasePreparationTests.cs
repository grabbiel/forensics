using Microsoft.Data.SqlClient;

namespace EvidenceChain.IntegrationTests;

[Collection(nameof(SqlCollection))]
public sealed class DatabasePreparationTests(ApiFactory factory)
{
    [Fact]
    public async Task Prepare_reruns_cleanly_and_leaves_RCSI_on()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? "");
        var cancellationToken = TestContext.Current.CancellationToken;

        await factory.PrepareAsync(cancellationToken);

        await using var connection = await OpenAsAppAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME();", connection);
        Assert.Equal(true, await command.ExecuteScalarAsync(cancellationToken));
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
    public async Task App_login_is_read_only_on_Day_1()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? "");
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var connection = await OpenAsAppAsync(cancellationToken);
        await using var read = new SqlCommand("SELECT COUNT(*) FROM dbo.Evidence;", connection);
        await using var delete = new SqlCommand("DELETE FROM dbo.Evidence;", connection);

        Assert.True((int)(await read.ExecuteScalarAsync(cancellationToken))! > 0);
        var error = await Assert.ThrowsAsync<SqlException>(() => delete.ExecuteNonQueryAsync(cancellationToken));
        Assert.Equal(229, error.Number); // permission denied on object
    }

    private async Task<SqlConnection> OpenAsAppAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(factory.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
