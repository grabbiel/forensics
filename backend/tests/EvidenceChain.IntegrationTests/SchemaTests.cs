using System.Security.Cryptography;
using System.Text;
using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.Custody;
using EvidenceChain.Domain.Notifications;
using EvidenceChain.Domain.People;
using EvidenceChain.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EvidenceChain.IntegrationTests;

/// <summary>The custody schema's rules, enforced by SQL Server itself (constraints, unique indexes, triggers).</summary>
[Collection(nameof(SqlCollection))]
public sealed class SchemaTests(ApiFactory factory)
{
    private const int Investigator = 1, Custodian = 4, OtherCustodian = 5;
    private static readonly DateTime Registered = new(2026, 10, 8, 9, 30, 0, DateTimeKind.Utc);
    private static Task<string>? _database;

    [Fact]
    public async Task Code_is_computed_and_timestamps_read_back_as_utc()
    {
        await using var db = await OpenAsync();
        var evidence = await AddEvidenceAsync(db, EvidenceTypes.Log, dailyNo: 7);

        await using var fresh = await OpenAsync();
        var stored = await fresh.Evidence.SingleAsync(e => e.EvidenceId == evidence.EvidenceId, Token);
        Assert.Equal("LOG202610080007", stored.Code);
        Assert.Equal((DateTimeKind.Utc, Registered), (stored.RegisteredAtUtc.Kind, stored.RegisteredAtUtc));
        Assert.Equal(stored.InitialCustodianId, stored.CurrentCustodianId);
    }

    [Fact]
    public async Task Content_over_8000_bytes_round_trips_with_its_hash()
    {
        await using var db = await OpenAsync();
        var bytes = RandomNumberGenerator.GetBytes(10_240);
        var evidence = await AddEvidenceAsync(db, EvidenceTypes.Eml, dailyNo: 2, bytes);

        await using var fresh = await OpenAsync();
        var content = await fresh.EvidenceContent.SingleAsync(c => c.EvidenceId == evidence.EvidenceId, Token);
        Assert.Equal(bytes, content.Bytes);
        Assert.Equal(SHA256.HashData(bytes), content.Sha256);
        Assert.Equal(10_240, content.ByteLength);
    }

    [Fact]
    public async Task Custody_events_are_append_only()
    {
        await using var db = await OpenAsync();
        var evidence = await AddEvidenceAsync(db, EvidenceTypes.Csv, dailyNo: 3);
        var genesis = await AddGenesisAsync(db, evidence);

        Assert.Equal(51000, await SqlErrorAsync(() => db.Database.ExecuteSqlAsync($"UPDATE dbo.CustodyEvents SET Notes = N'edited' WHERE CustodyEventId = {genesis.CustodyEventId}", Token)));
        Assert.Equal(51000, await SqlErrorAsync(() => db.Database.ExecuteSqlAsync($"DELETE dbo.CustodyEvents WHERE CustodyEventId = {genesis.CustodyEventId}", Token)));
    }

    [Fact]
    public async Task The_initial_custodian_never_changes_but_the_current_one_does()
    {
        await using var db = await OpenAsync();
        var evidence = await AddEvidenceAsync(db, EvidenceTypes.Log, dailyNo: 4);

        Assert.Equal(51001, await SqlErrorAsync(() => db.Database.ExecuteSqlAsync($"UPDATE dbo.Evidence SET InitialCustodianId = {OtherCustodian} WHERE EvidenceId = {evidence.EvidenceId}", Token)));
        Assert.Equal(1, await db.Database.ExecuteSqlAsync($"UPDATE dbo.Evidence SET CurrentCustodianId = {OtherCustodian} WHERE EvidenceId = {evidence.EvidenceId}", Token));
    }

    [Fact]
    public async Task Chains_have_one_genesis_linked_events_and_no_duplicate_positions()
    {
        await using var db = await OpenAsync();
        var evidence = await AddEvidenceAsync(db, EvidenceTypes.Log, dailyNo: 5);
        var genesis = await AddGenesisAsync(db, evidence);
        var transfer = await AddTransferAsync(db, evidence);

        // Sequence 2 without the previous MAC breaks the chain shape.
        Assert.Equal(547, await SqlErrorAsync(() => db.Database.ExecuteSqlAsync($"""
            INSERT dbo.CustodyEvents (EvidenceId, Seq, Kind, OccurredAtUtc, ActorId, TransferId, FromCustodianId, ToCustodianId, Notes, KeyId, Mac, CanonicalVersion)
            VALUES ({evidence.EvidenceId}, 2, 'TransferRequested', SYSUTCDATETIME(), {Investigator}, {transfer.TransferId}, {Custodian}, {OtherCustodian}, N'', 'dev', {Mac()}, 2)
            """, Token)));

        // A second event at the same position collides instead of forking the chain.
        db.CustodyEvents.Add(CustodyEvent.ForTransfer(evidence.EvidenceId, 2, CustodyEventKind.TransferRequested, Registered.AddHours(1), Investigator,
            transfer.TransferId, Custodian, OtherCustodian, "Análisis", "dev", genesis.Mac, Mac(), 2));
        await db.SaveChangesAsync(Token);
        db.CustodyEvents.Add(CustodyEvent.ForTransfer(evidence.EvidenceId, 2, CustodyEventKind.TransferRequested, Registered.AddHours(2), Investigator,
            transfer.TransferId, Custodian, OtherCustodian, "Fork", "dev", genesis.Mac, Mac(), 2));
        Assert.Equal(2601, await SqlErrorAsync(() => db.SaveChangesAsync(Token)));
    }

    [Fact]
    public async Task An_evidence_has_at_most_one_pending_transfer()
    {
        await using var db = await OpenAsync();
        var evidence = await AddEvidenceAsync(db, EvidenceTypes.Csv, dailyNo: 6);
        var first = await AddTransferAsync(db, evidence);

        await using (var second = await OpenAsync())
        {
            second.CustodyTransfers.Add(NewTransfer(evidence));
            Assert.Equal(2601, await SqlErrorAsync(() => second.SaveChangesAsync(Token)));
        }

        first.Accept(evidence, new Actor(OtherCustodian, UserRole.Custodio), Registered.AddHours(3), "Recibida", Guid.CreateVersion7(), Mac());
        await db.SaveChangesAsync(Token);
        var next = NewTransfer(evidence, to: Custodian); // allowed once the first is decided; custody moved to OtherCustodian
        db.CustodyTransfers.Add(next);
        await db.SaveChangesAsync(Token);
        Assert.Equal("Pending", await ScalarAsync<string>(db, $"SELECT Status AS Value FROM dbo.CustodyTransfers WHERE TransferId = {next.TransferId}"));
    }

    [Fact]
    public async Task Only_the_recipient_decides_and_idempotency_keys_are_unique()
    {
        await using var db = await OpenAsync();
        var evidence = await AddEvidenceAsync(db, EvidenceTypes.Eml, dailyNo: 7);
        var transfer = await AddTransferAsync(db, evidence);

        Assert.Equal(547, await SqlErrorAsync(() => db.Database.ExecuteSqlAsync($"""
            UPDATE dbo.CustodyTransfers SET Status = 'Accepted', DecidedAtUtc = SYSUTCDATETIME(), DecidedById = {Investigator}, DecisionKey = NEWID()
            WHERE TransferId = {transfer.TransferId}
            """, Token)));

        await using var replay = await OpenAsync();
        var duplicate = NewTransfer(evidence, key: transfer.ClientRequestId);
        replay.CustodyTransfers.Add(duplicate);
        Assert.Equal(2601, await SqlErrorAsync(() => replay.SaveChangesAsync(Token)));
    }

    [Fact]
    public async Task A_decided_transfer_needs_its_time_key_and_fingerprint()
    {
        await using var db = await OpenAsync();
        var evidence = await AddEvidenceAsync(db, EvidenceTypes.Log, dailyNo: 8);
        var transfer = await AddTransferAsync(db, evidence);

        // A NULL comparison is "unknown", which a CHECK would let through without the explicit IS NOT NULL.
        Assert.Equal(547, await SqlErrorAsync(() => db.Database.ExecuteSqlAsync($"""
            UPDATE dbo.CustodyTransfers SET Status = 'Accepted', DecidedById = {OtherCustodian}, DecisionKey = NEWID(), DecisionFingerprint = {Mac()}
            WHERE TransferId = {transfer.TransferId}
            """, Token)));
        Assert.Equal(547, await SqlErrorAsync(() => db.Database.ExecuteSqlAsync($"""
            UPDATE dbo.CustodyTransfers SET Status = 'Accepted', DecidedAtUtc = SYSUTCDATETIME(), DecidedById = {OtherCustodian}, DecisionKey = NEWID()
            WHERE TransferId = {transfer.TransferId}
            """, Token)));
    }

    [Fact]
    public async Task An_event_cannot_cite_another_evidence_transfer()
    {
        await using var db = await OpenAsync();
        var mine = await AddEvidenceAsync(db, EvidenceTypes.Csv, dailyNo: 9);
        var genesis = await AddGenesisAsync(db, mine);
        var other = await AddEvidenceAsync(db, EvidenceTypes.Csv, dailyNo: 10);
        var othersTransfer = await AddTransferAsync(db, other);

        db.CustodyEvents.Add(CustodyEvent.ForTransfer(mine.EvidenceId, 2, CustodyEventKind.TransferRequested, Registered.AddHours(1), Investigator,
            othersTransfer.TransferId, Custodian, OtherCustodian, "Ajena", "dev", genesis.Mac, Mac(), 2));
        Assert.Equal(547, await SqlErrorAsync(() => db.SaveChangesAsync(Token)));
    }

    [Fact]
    public async Task Rows_that_bypass_the_domain_still_meet_its_rules()
    {
        await using var db = await OpenAsync();
        var evidence = await AddEvidenceAsync(db, EvidenceTypes.Eml, dailyNo: 11);

        // The code date must be the UTC registration date.
        Assert.Equal(547, await SqlErrorAsync(() => db.Database.ExecuteSqlAsync($"""
            INSERT dbo.Evidence (TypeCode, CodeDateUtc, DailyNo, Description, CapturedAtUtc, RegisteredAtUtc, RegisteredById, InitialCustodianId, CurrentCustodianId)
            VALUES ('EML', '2026-10-07', 99, N'Fecha cambiada', '2026-10-08T08:00:00', '2026-10-08T09:00:00', {Investigator}, {Custodian}, {Custodian})
            """, Token)));

        // A genesis event must name the initial custodian.
        Assert.Equal(547, await SqlErrorAsync(() => db.Database.ExecuteSqlAsync($"""
            INSERT dbo.CustodyEvents (EvidenceId, Seq, Kind, OccurredAtUtc, ActorId, Notes, ContentSha256, ContentLength, MediaType, KeyId, Mac, CanonicalVersion)
            VALUES ({evidence.EvidenceId}, 1, 'EvidenceRegistered', SYSUTCDATETIME(), {Investigator}, N'', {Mac()}, 10, 'text/plain', 'dev', {Mac()}, 2)
            """, Token)));
    }

    [Fact]
    public async Task Only_utc_timestamps_are_written()
    {
        await using var db = await OpenAsync();
        db.SeedRuns.Add(new SeedRun { Seed = 42, AnchorUtc = new DateTime(2026, 10, 1), Profile = "reference", StartedAtUtc = DateTime.UtcNow });

        var error = await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync(Token));
        Assert.Contains("Only UTC timestamps", error.ToString());
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static byte[] Mac() => RandomNumberGenerator.GetBytes(32);

    [Fact]
    public async Task Inbox_keyset_indexes_include_every_column_a_page_reads_but_their_own_keys()
    {
        await using var db = await OpenAsync();
        var included = await db.Database.SqlQuery<string>($"""
            SELECT i.name + '|' + c.name AS Value
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID('dbo.EvidenceInbox')
            """).ToListAsync(Token);

        // What EvidenceInboxQuery selects besides the keyset (LastEventAtUtc, EvidenceId) every one of them ends with.
        string[] page = ["Code", "TypeCode", "Description", "CurrentCustodianId", "CurrentCustodianName", "EventCount",
            "IntegrityStatus", "IntegrityCheckedAtUtc", "PendingTransferId", "PendingToCustodianId", "PendingSinceUtc"];
        (string Index, string? Key)[] keyset =
            [("IX_EvidenceInbox_Recent", null), ("IX_EvidenceInbox_Type", "TypeCode"),
             ("IX_EvidenceInbox_Custodian", "CurrentCustodianId"), ("IX_EvidenceInbox_Integrity", "IntegrityStatus")];
        var expected = keyset.SelectMany(k => page.Where(c => c != k.Key).Select(c => $"{k.Index}|{c}"));
        Assert.Equal(expected.Order(StringComparer.Ordinal), included.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Notifications_store_exactly_the_kinds_the_domain_names_once_per_recipient_and_step()
    {
        await using var db = await OpenAsync();
        var evidence = await AddEvidenceAsync(db, EvidenceTypes.Log, dailyNo: 12);
        var transfer = await AddTransferAsync(db, evidence);

        foreach (var kind in Enum.GetNames<NotificationKind>())
            await db.Database.ExecuteSqlAsync($"INSERT dbo.Notifications (RecipientId, TransferId, Kind, CreatedAtUtc) VALUES ({Custodian}, {transfer.TransferId}, {kind}, SYSUTCDATETIME())", Token);
        Assert.Equal(547, await SqlErrorAsync(() => db.Database.ExecuteSqlAsync(
            $"INSERT dbo.Notifications (RecipientId, TransferId, Kind, CreatedAtUtc) VALUES ({Custodian}, {transfer.TransferId}, 'EvidenceRegistered', SYSUTCDATETIME())", Token)));
        Assert.Equal(2601, await SqlErrorAsync(() => db.Database.ExecuteSqlAsync(
            $"INSERT dbo.Notifications (RecipientId, TransferId, Kind, CreatedAtUtc) VALUES ({Custodian}, {transfer.TransferId}, 'TransferRequested', SYSUTCDATETIME())", Token)));
        Assert.Equal(547, await SqlErrorAsync(() => db.Database.ExecuteSqlAsync($"DELETE dbo.CustodyTransfers WHERE TransferId = {transfer.TransferId}", Token)));
    }

    [Fact]
    public async Task Notification_indexes_serve_the_list_and_the_unread_count()
    {
        await using var db = await OpenAsync();
        var indexes = await db.Database.SqlQuery<string>($"""
            SELECT i.name + '|' + IIF(i.is_unique = 1, 'unique', '') + '|'
                + (SELECT STRING_AGG(c.name + IIF(ic.is_descending_key = 1, ' desc', ''), ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
                   FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                   WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0) + '|'
                + ISNULL((SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY c.name)
                   FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                   WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1), '') + '|'
                + ISNULL(i.filter_definition, '') AS Value
            FROM sys.indexes i
            WHERE i.object_id = OBJECT_ID('dbo.Notifications') AND i.is_primary_key = 0
            """).ToListAsync(Token);

        Assert.Equal(
        [
            "IX_Notifications_Recipient||RecipientId,NotificationId desc|CreatedAtUtc,Kind,ReadAtUtc,TransferId|",
            "IX_Notifications_TransferId||TransferId||",
            "IX_Notifications_Unread||RecipientId||([ReadAtUtc] IS NULL)",
            "UX_Notifications_RecipientTransferKind|unique|RecipientId,TransferId,Kind||",
        ], indexes.Order(StringComparer.Ordinal));
    }

    private async Task<AppDbContext> OpenAsync()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? "");
        _database ??= PrepareAsync();
        return SqlServerSetup.CreateContext(await _database);
    }

    /// <summary>A dedicated database with the four users the tests act as.</summary>
    private async Task<string> PrepareAsync()
    {
        var connectionString = await factory.CreateDatabaseAsync("EvidenceChainSchemaTests");
        await using var db = SqlServerSetup.CreateContext(connectionString);
        db.Users.AddRange(
            new User(Investigator, "investigador.demo", "Lucía Ferrer", "investigador.demo@example.test", UserRole.Investigador),
            new User(Custodian, "custodio.demo", "Diego Salas", "custodio.demo@example.test", UserRole.Custodio),
            new User(OtherCustodian, "nuria.paredes", "Nuria Paredes", "nuria.paredes@example.test", UserRole.Custodio));
        await db.SaveChangesAsync();
        return connectionString;
    }

    private static async Task<Evidence> AddEvidenceAsync(AppDbContext db, string type, short dailyNo, byte[]? bytes = null)
    {
        var evidence = new Evidence(type, DateOnly.FromDateTime(Registered), dailyNo, $"Evidencia de prueba {type} {dailyNo}",
            Registered.AddHours(-2), Registered, Investigator, Custodian, new EvidenceContent(bytes ?? Encoding.UTF8.GetBytes("contenido\n"), "text/plain; charset=utf-8"));
        db.Evidence.Add(evidence);
        await db.SaveChangesAsync(Token);
        return evidence;
    }

    private static async Task<CustodyEvent> AddGenesisAsync(AppDbContext db, Evidence evidence)
    {
        var genesis = CustodyEvent.Genesis(evidence.EvidenceId, evidence.RegisteredAtUtc, Investigator, Custodian,
            evidence.Content.Sha256, evidence.Content.ByteLength, evidence.Content.MediaType, "Registro inicial", "dev", Mac(), 2);
        db.CustodyEvents.Add(genesis);
        evidence.AdvanceHead(1, genesis.Mac);
        await db.SaveChangesAsync(Token);
        return genesis;
    }

    /// <summary>A request from the evidence's current custodian; pending checks are left to the database here.</summary>
    private static CustodyTransfer NewTransfer(Evidence evidence, int to = OtherCustodian, Guid? key = null) =>
        CustodyTransfer.Request(evidence, new Actor(Investigator, UserRole.Investigador), new Actor(to, UserRole.Custodio), pendingTransfer: null,
            Registered.AddHours(1), "Análisis en laboratorio", key ?? Guid.CreateVersion7(), Mac());

    private static async Task<CustodyTransfer> AddTransferAsync(AppDbContext db, Evidence evidence)
    {
        var transfer = NewTransfer(evidence);
        db.CustodyTransfers.Add(transfer);
        await db.SaveChangesAsync(Token);
        return transfer;
    }

    private static async Task<T> ScalarAsync<T>(AppDbContext db, FormattableString sql) =>
        await db.Database.SqlQuery<T>(sql).SingleAsync(Token);

    /// <summary>Runs the action and returns the SQL Server error number it fails with.</summary>
    private static async Task<int> SqlErrorAsync(Func<Task> action)
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(action);
        var sql = error as SqlException ?? error.InnerException as SqlException;
        Assert.NotNull(sql);
        return sql.Number;
    }
}
