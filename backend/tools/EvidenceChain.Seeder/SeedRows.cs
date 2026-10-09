using System.Data;
using System.Security.Cryptography;
using System.Text;
using EvidenceChain.Domain.Custody;
using EvidenceChain.Domain.Integrity;
using EvidenceChain.SyntheticData;

namespace EvidenceChain.Seeder;

/// <summary>
/// A synthetic dataset turned into table rows: ids assigned in a fixed order, chains MAC'd with the active key,
/// and the inbox projection and daily counters derived. Identical inputs give identical rows.
/// </summary>
internal sealed class SeedRows
{
    public required DataTable Users { get; init; }
    public required DataTable Evidence { get; init; }
    public required DataTable EvidenceContent { get; init; }
    public required DataTable CustodyTransfers { get; init; }
    public required DataTable CustodyEvents { get; init; }
    public required DataTable DailyCounter { get; init; }
    public required DataTable EvidenceInbox { get; init; }

    /// <summary>Evidence id per generated code, in registration order from 1.</summary>
    public required IReadOnlyDictionary<string, long> EvidenceIds { get; init; }

    /// <summary>Builds every row; ids follow the dataset's registration and chain order.</summary>
    public static SeedRows Build(SyntheticDataset dataset, IntegrityKeyRing keys)
    {
        var userIds = dataset.Users.ToDictionary(u => u.UserName, u => u.Id);
        var names = dataset.Users.ToDictionary(u => u.UserName, u => u.DisplayName);
        var evidenceIds = dataset.Evidences.Select((e, i) => (e.Code, Id: (long)i + 1)).ToDictionary(x => x.Code, x => x.Id);
        var transfersByNumber = dataset.Transfers.ToDictionary(t => t.Number);
        var chains = dataset.Events.GroupBy(e => e.EvidenceCode).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Seq).ToArray());
        var pending = dataset.Transfers.Where(t => t.Status == SyntheticTransferStatus.Pending).ToDictionary(t => t.EvidenceCode);

        var users = Table(("UserId", typeof(int)), ("UserName", typeof(string)), ("DisplayName", typeof(string)), ("Email", typeof(string)), ("Role", typeof(string)));
        foreach (var u in dataset.Users)
            users.Rows.Add(u.Id, u.UserName, u.DisplayName, u.Email, u.Role.ToString());

        var evidence = Table(("EvidenceId", typeof(long)), ("TypeCode", typeof(string)), ("CodeDateUtc", typeof(DateTime)), ("DailyNo", typeof(short)),
            ("Description", typeof(string)), ("CapturedAtUtc", typeof(DateTime)), ("RegisteredAtUtc", typeof(DateTime)), ("RegisteredById", typeof(int)),
            ("InitialCustodianId", typeof(int)), ("CurrentCustodianId", typeof(int)), ("HeadMac", typeof(byte[])), ("EventCount", typeof(int)), ("IsDemoFixture", typeof(bool)));
        var content = Table(("EvidenceId", typeof(long)), ("Sha256", typeof(byte[])), ("ByteLength", typeof(int)), ("MediaType", typeof(string)), ("Bytes", typeof(byte[])));
        var transfers = Table(("TransferId", typeof(long)), ("EvidenceId", typeof(long)), ("FromCustodianId", typeof(int)), ("ToCustodianId", typeof(int)),
            ("RequestedById", typeof(int)), ("RequestedAtUtc", typeof(DateTime)), ("Reason", typeof(string)), ("Status", typeof(string)), ("DecidedAtUtc", typeof(DateTime)),
            ("DecidedById", typeof(int)), ("DecisionNotes", typeof(string)), ("ClientRequestId", typeof(Guid)), ("RequestFingerprint", typeof(byte[])),
            ("DecisionKey", typeof(Guid)), ("DecisionFingerprint", typeof(byte[])));
        var events = Table(("CustodyEventId", typeof(long)), ("EvidenceId", typeof(long)), ("Seq", typeof(int)), ("Kind", typeof(string)), ("OccurredAtUtc", typeof(DateTime)),
            ("ActorId", typeof(int)), ("TransferId", typeof(long)), ("FromCustodianId", typeof(int)), ("ToCustodianId", typeof(int)), ("Notes", typeof(string)),
            ("ContentSha256", typeof(byte[])), ("ContentLength", typeof(int)), ("MediaType", typeof(string)), ("KeyId", typeof(string)), ("PrevMac", typeof(byte[])),
            ("Mac", typeof(byte[])), ("CanonicalVersion", typeof(byte)));
        var inbox = Table(("EvidenceId", typeof(long)), ("Code", typeof(string)), ("TypeCode", typeof(string)), ("CodeDateUtc", typeof(DateTime)), ("Description", typeof(string)),
            ("RegisteredAtUtc", typeof(DateTime)), ("CurrentCustodianId", typeof(int)), ("CurrentCustodianName", typeof(string)), ("EventCount", typeof(int)),
            ("LastEventAtUtc", typeof(DateTime)), ("IntegrityStatus", typeof(string)), ("PendingTransferId", typeof(long)), ("PendingToCustodianId", typeof(int)),
            ("PendingSinceUtc", typeof(DateTime)));

        foreach (var t in dataset.Transfers.OrderBy(t => t.Number))
        {
            var decided = t.Status != SyntheticTransferStatus.Pending;
            transfers.Rows.Add(t.Number, evidenceIds[t.EvidenceCode], userIds[t.FromCustodian], userIds[t.ToCustodian], userIds[t.RequestedBy], t.RequestedAtUtc,
                t.Reason, t.Status.ToString(), Db(t.DecidedAtUtc), decided ? userIds[t.ToCustodian] : DBNull.Value, Db(t.DecisionNotes), t.ClientRequestId,
                Fingerprint(t.EvidenceCode, t.ToCustodian, t.Reason), Db(t.DecisionKey), decided ? Fingerprint(t.Status.ToString(), t.DecisionNotes ?? "") : DBNull.Value);
        }

        long eventId = 0;
        foreach (var e in dataset.Evidences)
        {
            var id = evidenceIds[e.Code];
            byte[]? previous = null;
            foreach (var step in chains[e.Code])
            {
                var transfer = step.TransferNumber is { } n ? transfersByNumber[n] : null;
                var genesis = step.Kind == SyntheticEventKind.EvidenceRegistered;
                var link = new ChainLink(e.Code, step.Seq, Enum.Parse<CustodyEventKind>(step.Kind.ToString()), step.OccurredAtUtc, userIds[step.Actor],
                    transfer is null ? null : userIds[transfer.FromCustodian],
                    genesis ? userIds[e.InitialCustodian] : userIds[transfer!.ToCustodian],
                    step.Notes, genesis ? e.Sha256 : null, genesis ? e.Content.Length : null, genesis ? e.MediaType : null, keys.ActiveKeyId, previous);
                var mac = ChainHasher.Mac(keys, link);
                events.Rows.Add(++eventId, id, link.Seq, link.Kind.ToString(), link.OccurredAtUtc, link.ActorId, Db(step.TransferNumber is { } t ? (long?)t : null),
                    Db(link.FromCustodianId), Db(link.ToCustodianId), link.Notes, Db(link.ContentSha256), Db(link.ContentLength), Db(link.MediaType),
                    link.KeyId, Db(link.PrevMac), mac, CanonicalEvent.Version);
                previous = mac;
            }

            evidence.Rows.Add(id, e.TypeCode, e.CodeDateUtc.ToDateTime(TimeOnly.MinValue), (short)e.DailyNo, e.Description, e.CapturedAtUtc, e.RegisteredAtUtc,
                userIds[e.RegisteredBy], userIds[e.InitialCustodian], userIds[e.CurrentCustodian], previous!, e.EventCount, e.Fixture is not null);
            content.Rows.Add(id, e.Sha256, e.Content.Length, e.MediaType, e.Content);

            var open = pending.GetValueOrDefault(e.Code);
            inbox.Rows.Add(id, e.Code, e.TypeCode, e.CodeDateUtc.ToDateTime(TimeOnly.MinValue), e.Description, e.RegisteredAtUtc, userIds[e.CurrentCustodian],
                names[e.CurrentCustodian], e.EventCount, chains[e.Code][^1].OccurredAtUtc, nameof(IntegrityStatus.Unverified),
                Db(open is null ? null : (long?)open.Number), Db(open is null ? null : (int?)userIds[open.ToCustodian]), Db(open?.RequestedAtUtc));
        }

        var counters = Table(("TypeCode", typeof(string)), ("Day", typeof(DateTime)), ("LastNo", typeof(short)));
        foreach (var day in dataset.Evidences.GroupBy(e => (e.TypeCode, e.CodeDateUtc)).OrderBy(g => g.Key.TypeCode, StringComparer.Ordinal).ThenBy(g => g.Key.CodeDateUtc))
            counters.Rows.Add(day.Key.TypeCode, day.Key.CodeDateUtc.ToDateTime(TimeOnly.MinValue), (short)day.Max(e => e.DailyNo));

        return new SeedRows
        {
            Users = users, Evidence = evidence, EvidenceContent = content, CustodyTransfers = transfers, CustodyEvents = events,
            DailyCounter = counters, EvidenceInbox = inbox, EvidenceIds = evidenceIds,
        };
    }

    /// <summary>SHA-256 over newline-joined fields; the seeded stand-in for a request body's fingerprint.</summary>
    private static byte[] Fingerprint(params string[] fields) => SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', fields)));

    private static object Db(object? value) => value ?? DBNull.Value;

    private static DataTable Table(params (string Name, Type Type)[] columns)
    {
        var table = new DataTable();
        foreach (var (name, type) in columns)
            table.Columns.Add(name, type);
        return table;
    }
}
