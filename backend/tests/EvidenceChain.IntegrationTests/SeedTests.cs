extern alias seeder;

using System.Security.Cryptography;
using EvidenceChain.Domain.Anomalies;
using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.Integrity;
using EvidenceChain.Infrastructure.Persistence;
using EvidenceChain.SyntheticData;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using DatasetLoader = seeder::EvidenceChain.Seeder.DatasetLoader;
using SeedMode = seeder::EvidenceChain.Seeder.SeedMode;

namespace EvidenceChain.IntegrationTests;

/// <summary>Loads the reference dataset into fresh databases, checks what landed and runs the domain rules over it.</summary>
[Collection(nameof(SqlCollection))]
public sealed class SeedTests(ApiFactory factory)
{
    private static readonly IntegrityKeyRing Keys = ReferenceData.Keys;
    private static readonly Lazy<SyntheticDataset> Dataset = ReferenceData.Dataset;
    private static Task<(string A, string B)>? _databases;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_fresh_loads_are_identical_and_match_the_reference_manifest()
    {
        var (a, b) = await SeededAsync();
        var (evidenceA, chainA) = await SnapshotAsync(a);
        var (evidenceB, chainB) = await SnapshotAsync(b);

        Assert.Equal(1_000, evidenceA.Count);
        Assert.Equal(evidenceA, evidenceB); // same ids, hashes, counts and head MACs
        Assert.Equal(chainA, chainB); // same event and transfer ids, sequences and MACs

        var manifest = File.ReadLines(RepoFile("database", "synthetic", ReferenceDataset.ManifestFileName)).Skip(1)
            .Select(line => line.Split(',')).ToDictionary(f => f[0], f => (Sha256: f[6], Events: int.Parse(f[13])));
        Assert.All(evidenceA, row => Assert.Equal(manifest[row.Key], (row.Value.Sha256, row.Value.Events)));
    }

    [Fact]
    public async Task Every_stored_mac_recomputes_except_the_tampered_event()
    {
        var (a, _) = await SeededAsync();
        await using var db = SqlServerSetup.CreateContext(a);
        var codes = await db.Evidence.ToDictionaryAsync(e => e.EvidenceId, e => e.Code, Token);
        var events = await db.CustodyEvents.AsNoTracking().OrderBy(e => e.EvidenceId).ThenBy(e => e.Seq).ToListAsync(Token);

        var broken = new List<(string Code, int Seq)>();
        byte[]? previous = null;
        foreach (var e in events)
        {
            if (e.Seq == 1)
                previous = null;
            Assert.Equal(previous, e.PrevMac); // links point at the previous stored MAC
            if (!ChainHasher.Matches(ChainHasher.Mac(Keys, ChainLink.From(codes[e.EvidenceId], e)), e.Mac))
                broken.Add((codes[e.EvidenceId], e.Seq));
            previous = e.Mac;
        }

        Assert.Equal(10_000, events.Count);
        Assert.Equal([(Dataset.Value.Fixtures.EventTampered, Dataset.Value.Fixtures.EventTamperedSeq)], broken);
    }

    [Fact]
    public async Task Tamper_fixtures_are_in_place_with_constraints_trusted_and_the_trigger_back_on()
    {
        var (a, _) = await SeededAsync();
        var f = Dataset.Value.Fixtures;
        await using var db = SqlServerSetup.CreateContext(a);

        var content = await db.Evidence.Include(e => e.Content).SingleAsync(e => e.Code == f.ContentTampered, Token);
        var genesis = await db.CustodyEvents.SingleAsync(e => e.EvidenceId == content.EvidenceId && e.Seq == 1, Token);
        Assert.NotEqual(genesis.ContentSha256, SHA256.HashData(content.Content.Bytes)); // bytes no longer match the commitment
        Assert.Equal(genesis.ContentLength, content.Content.Bytes.Length);

        var custodian = await db.Evidence.SingleAsync(e => e.Code == f.CustodianTampered, Token);
        Assert.Equal(Dataset.Value.Users.Single(u => u.UserName == f.CustodianTamperedTo).Id, custodian.CurrentCustodianId);

        Assert.Equal(0, await CountAsync(db, "SELECT COUNT(*) AS Value FROM sys.check_constraints WHERE is_not_trusted = 1"));
        Assert.Equal(0, await CountAsync(db, "SELECT COUNT(*) AS Value FROM sys.foreign_keys WHERE is_not_trusted = 1"));
        Assert.Equal(0, await CountAsync(db, "SELECT COUNT(*) AS Value FROM sys.triggers WHERE is_disabled = 1"));
    }

    [Fact]
    public async Task The_verifier_passes_every_evidence_except_the_three_tamper_fixtures()
    {
        var (a, _) = await SeededAsync();
        var f = Dataset.Value.Fixtures;
        await using var db = SqlServerSetup.CreateContext(a);
        var evidences = await db.Evidence.AsNoTracking().Include(e => e.Content).ToListAsync(Token);
        var chains = (await db.CustodyEvents.AsNoTracking().OrderBy(e => e.Seq).ToListAsync(Token)).ToLookup(e => e.EvidenceId);
        var transfers = (await db.CustodyTransfers.AsNoTracking().ToListAsync(Token)).ToLookup(t => t.EvidenceId);

        var failures = evidences
            .Select(e => (e.Code, Verdict: ChainVerifier.Verify(Keys, e, chains[e.EvidenceId].ToList(), transfers[e.EvidenceId].ToList(), e.Content)))
            .Where(r => !r.Verdict.IsValid)
            .ToDictionary(r => r.Code, r => (r.Verdict.Failure!.Value.Code(), r.Verdict.FailedAtSeq!.Value));

        Assert.Equal(997, evidences.Count - failures.Count);
        Assert.Equal(new Dictionary<string, (string, int)>
        {
            [f.EventTampered] = ("MAC_MISMATCH", f.EventTamperedSeq),
            [f.ContentTampered] = ("CONTENT_HASH_MISMATCH", 1),
            [f.CustodianTampered] = ("CUSTODY_PROJECTION_MISMATCH", evidences.Single(e => e.Code == f.CustodianTampered).EventCount),
        }, failures);
    }

    [Fact]
    public async Task At_the_anchor_only_the_overdue_fixture_and_the_late_acceptances_are_flagged()
    {
        var (a, _) = await SeededAsync();
        var f = Dataset.Value.Fixtures;
        await using var db = SqlServerSetup.CreateContext(a);
        var codes = await db.Evidence.ToDictionaryAsync(e => e.EvidenceId, e => e.Code, Token);
        var transfers = await db.CustodyTransfers.AsNoTracking().ToListAsync(Token);
        var rule = new OverdueTransferRule(TimeSpan.FromHours(48), new FixedClock(Dataset.Value.AnchorUtc));

        var findings = transfers
            .Select(t => (Code: codes[t.EvidenceId], Finding: rule.Evaluate(t)))
            .Where(r => r.Finding is not null)
            .Select(r => (r.Code, r.Finding!.Kind, r.Finding.Severity))
            .OrderBy(r => r.Code, StringComparer.Ordinal).ToList();

        var expected = f.AcceptedLate.Select(code => (code, TransferAnomalyKind.AcceptedLate, AnomalySeverity.Medium))
            .Append((f.OverdueTransfer, TransferAnomalyKind.Overdue, AnomalySeverity.Medium))
            .OrderBy(r => r.Item1, StringComparer.Ordinal).ToList();
        Assert.Equal(expected, findings);
    }

    [Fact]
    public async Task Reseeding_is_a_no_op_refused_or_a_full_reload_and_new_rows_continue_the_ids()
    {
        var connectionString = await factory.CreateDatabaseAsync("EvidenceChainSeedRerun", Token);
        var first = await DatasetLoader.SeedAsync(connectionString, Dataset.Value, Keys, SeedMode.Strict, "seed", Token);
        Assert.Equal((false, 1_000, 10_000), (first.AlreadySeeded, first.Evidences, first.Events));

        Assert.True((await DatasetLoader.SeedAsync(connectionString, Dataset.Value, Keys, SeedMode.IfEmpty, "seed --if-empty", Token)).AlreadySeeded);
        await Assert.ThrowsAsync<ArgumentException>(() => DatasetLoader.SeedAsync(connectionString, Dataset.Value, Keys, SeedMode.Strict, "seed", Token));

        // A run that fails part-way changes nothing: the failure happens after the reset inside the same transaction.
        var otherKeys = new IntegrityKeyRing("other", new Dictionary<string, byte[]> { ["other"] = new byte[32] });
        var broken = Dataset.Value with { Fixtures = Dataset.Value.Fixtures with { ContentTampered = "NOPE20260101" + "0001" } };
        await Assert.ThrowsAnyAsync<Exception>(() => DatasetLoader.SeedAsync(connectionString, broken, otherKeys, SeedMode.Reset, "seed --reset", Token));
        Assert.True((await DatasetLoader.SeedAsync(connectionString, Dataset.Value, Keys, SeedMode.IfEmpty, "seed --if-empty", Token)).AlreadySeeded);

        var reloaded = await DatasetLoader.SeedAsync(connectionString, Dataset.Value, Keys, SeedMode.Reset, "seed --reset", Token);
        Assert.Equal(10_000, reloaded.Events);

        await using var db = SqlServerSetup.CreateContext(connectionString);
        Assert.Equal(1, await CountAsync(db, "SELECT COUNT(*) AS Value FROM dbo.SeedRuns"));
        var registered = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var next = new Evidence(EvidenceTypes.Log, DateOnly.FromDateTime(registered), 1, "Registrada tras la carga", registered, registered,
            registeredById: 1, initialCustodianId: 4, new EvidenceContent([1], "text/plain"));
        db.Evidence.Add(next);
        await db.SaveChangesAsync(Token);
        Assert.Equal(1_001, next.EvidenceId);

    }

    [Fact]
    public async Task Writes_after_the_seed_keep_it_but_a_missing_seeded_row_is_refused_even_when_masked()
    {
        const string database = "EvidenceChainSeedAfterWrites";
        var api = await factory.SeededApiAsync(database);
        var admin = factory.ConnectionStringFor(database);

        // One custody request through the API appends an event past the seeded ones.
        var evidence = Dataset.Value.Evidences.First(e => e.Fixture is null
            && !Dataset.Value.Transfers.Any(t => t.EvidenceCode == e.Code && t.Status == SyntheticTransferStatus.Pending));
        var recipient = SyntheticPeople.All.First(u => u.Role == SyntheticRole.Custodio && u.UserName != evidence.CurrentCustodian);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/custody-transfers")
        {
            Content = JsonContent.Create(new { evidenceCode = evidence.Code, toCustodianId = recipient.Id, reason = "Análisis" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Created, (await api.CreateClientAs(TestUsers.Investigator).SendAsync(request, Token)).StatusCode);
        Assert.True((await DatasetLoader.SeedAsync(admin, Dataset.Value, Keys, SeedMode.IfEmpty, "seed --if-empty", Token)).AlreadySeeded);

        // A seeded event removed behind the append-only trigger, the total still above the seed's: refused.
        await using (var db = SqlServerSetup.CreateContext(admin))
            await db.Database.ExecuteSqlRawAsync("""
                DISABLE TRIGGER dbo.TR_CustodyEvents_AppendOnly ON dbo.CustodyEvents;
                DELETE dbo.CustodyEvents WHERE CustodyEventId = 2;
                ENABLE TRIGGER dbo.TR_CustodyEvents_AppendOnly ON dbo.CustodyEvents;
                """, Token);
        var error = await Assert.ThrowsAsync<ArgumentException>(() => DatasetLoader.SeedAsync(admin, Dataset.Value, Keys, SeedMode.IfEmpty, "seed --if-empty", Token));
        Assert.Contains("1 of its events are gone", error.Message);
    }

    /// <summary>Two databases loaded once with the reference dataset and shared by the read-only tests.</summary>
    private Task<(string A, string B)> SeededAsync()
    {
        Assert.SkipWhen(factory.SkipReason is not null, factory.SkipReason ?? "");
        return _databases ??= LoadBothAsync();

        async Task<(string, string)> LoadBothAsync()
        {
            var a = await factory.CreateDatabaseAsync("EvidenceChainSeedA");
            var b = await factory.CreateDatabaseAsync("EvidenceChainSeedB");
            await DatasetLoader.SeedAsync(a, Dataset.Value, Keys, SeedMode.Strict, "seed", CancellationToken.None);
            await DatasetLoader.SeedAsync(b, Dataset.Value, Keys, SeedMode.Strict, "seed", CancellationToken.None);
            return (a, b);
        }
    }

    /// <summary>Per code: id, content SHA-256, event count and head MAC; plus every event's ids, position and MAC in order.</summary>
    private static async Task<(Dictionary<string, (string Sha256, int Events, long Id, string HeadMac)> Evidence, List<(long Id, long EvidenceId, int Seq, long? TransferId, string Mac)> Chain)> SnapshotAsync(string connectionString)
    {
        await using var db = SqlServerSetup.CreateContext(connectionString);
        var evidence = await db.Evidence.AsNoTracking().Include(e => e.Content).ToListAsync(Token);
        var events = await db.CustodyEvents.AsNoTracking().OrderBy(e => e.CustodyEventId).ToListAsync(Token);
        return (evidence.ToDictionary(e => e.Code, e => (Convert.ToHexStringLower(e.Content.Sha256), e.EventCount, e.EvidenceId, Convert.ToHexStringLower(e.HeadMac!))),
            events.Select(e => (e.CustodyEventId, e.EvidenceId, e.Seq, e.TransferId, Convert.ToHexStringLower(e.Mac))).ToList());
    }

    private static async Task<int> CountAsync(AppDbContext db, string sql) =>
        await db.Database.SqlQueryRaw<int>(sql).SingleAsync(Token);

    private static string RepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "database")) && Directory.Exists(Path.Combine(dir.FullName, "backend")))
                return Path.Combine([dir.FullName, .. parts]);
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }

}
