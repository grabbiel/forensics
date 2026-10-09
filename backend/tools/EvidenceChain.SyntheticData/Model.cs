namespace EvidenceChain.SyntheticData;

/// <summary>Application roles of the seeded people.</summary>
public enum SyntheticRole
{
    Investigador,
    Custodio,
    Supervisor,
}

/// <summary>A seeded person; <see cref="UserName"/> is the stable key used everywhere else.</summary>
public sealed record SyntheticUser(int Id, string UserName, string DisplayName, SyntheticRole Role)
{
    /// <summary>Always under example.test, so no real mailbox is ever addressed.</summary>
    public string Email => $"{UserName}@example.test";
}

/// <summary>One evidence file with its identity, bytes and custody summary.</summary>
public sealed record SyntheticEvidence(
    string Code,
    string TypeCode,
    DateOnly CodeDateUtc,
    int DailyNo,
    string FileName,
    string MediaType,
    byte[] Content,
    byte[] Sha256,
    string Description,
    DateTime CapturedAtUtc,
    DateTime RegisteredAtUtc,
    string RegisteredBy,
    string InitialCustodian,
    string CurrentCustodian,
    int EventCount,
    string? Fixture);

/// <summary>Transfer outcome; pending transfers have no decision yet.</summary>
public enum SyntheticTransferStatus
{
    Pending,
    Accepted,
    Rejected,
}

/// <summary>A custody transfer between two Custodios, requested by an Investigador.</summary>
public sealed record SyntheticTransfer(
    int Number,
    string EvidenceCode,
    int RequestSeq,
    string FromCustodian,
    string ToCustodian,
    string RequestedBy,
    DateTime RequestedAtUtc,
    string Reason,
    Guid ClientRequestId,
    SyntheticTransferStatus Status,
    DateTime? DecidedAtUtc,
    string? DecisionNotes,
    Guid? DecisionKey);

/// <summary>Kinds of custody events in an evidence's chain.</summary>
public enum SyntheticEventKind
{
    EvidenceRegistered,
    TransferRequested,
    TransferAccepted,
    TransferRejected,
}

/// <summary>One link of a custody chain; MACs are added by the seeder, which holds the key.</summary>
public sealed record SyntheticEvent(
    string EvidenceCode,
    int Seq,
    SyntheticEventKind Kind,
    DateTime OccurredAtUtc,
    string Actor,
    int? TransferNumber,
    string Notes);

/// <summary>Evidences chosen to demonstrate specific behaviours; the README lists their codes.</summary>
public sealed record DatasetFixtures(
    string Intact,
    string OverdueTransfer,
    string FreshPending,
    string LargeEmail,
    string EventTampered,
    int EventTamperedSeq,
    string ContentTampered,
    int ContentTamperedOffset,
    string CustodianTampered,
    string CustodianTamperedTo,
    IReadOnlyList<string> AcceptedLate);

/// <summary>The whole generated dataset, in memory.</summary>
public sealed record SyntheticDataset(
    ulong Seed,
    DateTime AnchorUtc,
    DatasetProfile Profile,
    IReadOnlyList<SyntheticUser> Users,
    IReadOnlyList<SyntheticEvidence> Evidences,
    IReadOnlyList<SyntheticTransfer> Transfers,
    IReadOnlyList<SyntheticEvent> Events,
    DatasetFixtures Fixtures);
