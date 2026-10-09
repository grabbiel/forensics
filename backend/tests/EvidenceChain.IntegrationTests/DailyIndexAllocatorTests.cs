using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.People;
using EvidenceChain.Infrastructure.Catalog;
using EvidenceChain.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvidenceChain.IntegrationTests;

[Collection(nameof(SqlCollection))]
public sealed class DailyIndexAllocatorTests(ApiFactory factory)
{
    private static Task<string>? _database;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Indexes_count_from_one_per_type_and_day()
    {
        var day = new DateOnly(2026, 1, 10);
        var first = await AllocateAsync(EvidenceTypes.Log, day);
        var second = await AllocateAsync(EvidenceTypes.Log, day);
        var third = await AllocateAsync(EvidenceTypes.Log, day);
        Assert.Equal((1, 2, 3), (first, second, third));
        Assert.Equal(1, await AllocateAsync(EvidenceTypes.Csv, day));
        Assert.Equal(1, await AllocateAsync(EvidenceTypes.Log, day.AddDays(1)));
    }

    [Fact]
    public async Task A_rolled_back_registration_returns_its_index()
    {
        var day = new DateOnly(2026, 1, 11);
        await using (var db = await OpenAsync())
        await using (await db.Database.BeginTransactionAsync(Token))
        {
            Assert.Equal(1, await DailyIndexAllocator.NextAsync(db, EvidenceTypes.Eml, day, Token));
            // disposed without commit: rolled back
        }

        Assert.Equal(1, await AllocateAsync(EvidenceTypes.Eml, day));
    }

    [Fact]
    public async Task Concurrent_first_registrations_of_a_day_get_distinct_indexes()
    {
        var day = new DateOnly(2026, 1, 12);
        var indexes = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => AllocateAsync(EvidenceTypes.Log, day))));
        Assert.Equal(Enumerable.Range(1, 16).Select(i => (short)i), indexes.Order());
    }

    [Fact]
    public async Task Concurrent_registrations_allocate_and_save_in_one_retriable_transaction()
    {
        // The pattern a registration must follow with EnableRetryOnFailure: allocate, save and commit
        // as one unit inside the execution strategy, so a retry replays all of it.
        var registered = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var codes = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
        {
            await using var db = await OpenAsync();
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(Token);
                var dailyNo = await DailyIndexAllocator.NextAsync(db, EvidenceTypes.Csv, DateOnly.FromDateTime(registered), Token);
                var evidence = new Evidence(EvidenceTypes.Csv, DateOnly.FromDateTime(registered), dailyNo, $"Registro concurrente {i}",
                    registered.AddHours(-1), registered, registeredById: 1, initialCustodianId: 4, new EvidenceContent([(byte)i], "text/csv; charset=utf-8"));
                db.Evidence.Add(evidence);
                await db.SaveChangesAsync(Token);
                await transaction.CommitAsync(Token);
                return evidence.Code;
            });
        })));

        Assert.Equal(Enumerable.Range(1, 8).Select(n => $"CSV20260115{n:D4}"), codes.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_exhausted_day_fails_before_any_code_is_formatted()
    {
        var day = new DateOnly(2026, 1, 13);
        await using (var db = await OpenAsync())
        {
            db.DailyCounters.Add(new DailyCounter(EvidenceTypes.Csv, day, EvidenceCode.MaxDailyNo));
            await db.SaveChangesAsync(Token);
        }

        var error = await Assert.ThrowsAsync<DailyIndexExhaustedException>(() => AllocateAsync(EvidenceTypes.Csv, day));
        Assert.Equal((EvidenceTypes.Csv, day), (error.TypeCode, error.Day));
    }

    [Fact]
    public async Task Allocation_outside_a_transaction_is_refused()
    {
        await using var db = await OpenAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => DailyIndexAllocator.NextAsync(db, EvidenceTypes.Log, new DateOnly(2026, 1, 14), Token));
    }

    /// <summary>One allocation in its own committed transaction, as a registration would do it.</summary>
    private async Task<short> AllocateAsync(string type, DateOnly day)
    {
        await using var db = await OpenAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        var next = await DailyIndexAllocator.NextAsync(db, type, day, Token);
        await transaction.CommitAsync(Token);
        return next;
    }

    private async Task<AppDbContext> OpenAsync()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? "");
        _database ??= PrepareAsync();
        return SqlServerSetup.CreateContext(await _database);
    }

    private async Task<string> PrepareAsync()
    {
        var connectionString = await factory.CreateDatabaseAsync("EvidenceChainAllocatorTests");
        await using var db = SqlServerSetup.CreateContext(connectionString);
        db.Users.AddRange(
            new User(1, "investigador.demo", "Lucía Ferrer", "investigador.demo@example.test", UserRole.Investigador),
            new User(4, "custodio.demo", "Diego Salas", "custodio.demo@example.test", UserRole.Custodio));
        await db.SaveChangesAsync();
        return connectionString;
    }
}
