namespace EvidenceChain.SyntheticData;

/// <summary>Dataset size: evidences, exact event total, calendar length and the per-evidence transfer cap.</summary>
public sealed record DatasetProfile(string Name, int Evidences, int TotalEvents, int Days, int MaxTransfersPerEvidence)
{
    /// <summary>The brief's dataset: 1,000 evidences and 10,000 events; the committed reference uses it.</summary>
    public static DatasetProfile Reference { get; } = new("reference", 1_000, 10_000, 90, 10);

    /// <summary>Load-testing dataset: 20,000 evidences and 200,000 events, same rules and fixtures.</summary>
    public static DatasetProfile Scale { get; } = new("scale", 20_000, 200_000, 90, 10);

    /// <summary>Name and every size, e.g. "reference:1000/10000/10/90"; seed runs compare on it.</summary>
    public string Spec => FormattableString.Invariant($"{Name}:{Evidences}/{TotalEvents}/{MaxTransfersPerEvidence}/{Days}");

    /// <summary>Named profiles, for the command line.</summary>
    public static IReadOnlyList<DatasetProfile> Named { get; } = [Reference, Scale];

    /// <summary>Evidences per type: 40% .log, 30% .csv, the rest .eml (400/300/300 for the reference).</summary>
    public IReadOnlyList<(string Type, int Count)> TypeSplit
    {
        get
        {
            var logs = (int)Math.Round(Evidences * 0.4, MidpointRounding.AwayFromZero);
            var csvs = (int)Math.Round(Evidences * 0.3, MidpointRounding.AwayFromZero);
            return [(Domain.Catalog.EvidenceTypes.Log, logs), (Domain.Catalog.EvidenceTypes.Csv, csvs), (Domain.Catalog.EvidenceTypes.Eml, Evidences - logs - csvs)];
        }
    }

    /// <summary>Ordinary pending transfers besides the two pending fixtures: one per 500 evidences, at least two.</summary>
    public int OrdinaryPending
    {
        get
        {
            var ordinary = Math.Max(2, Evidences / 500);
            // A decided transfer adds two events and a pending one adds one, so the parity must work out.
            return (TotalEvents - Evidences - (ordinary + 2)) % 2 == 0 ? ordinary : ordinary + 1;
        }
    }

    /// <summary>Throws when the numbers cannot produce a valid dataset.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Evidences, 100, nameof(Evidences)); // room for every fixture
        ArgumentOutOfRangeException.ThrowIfLessThan(Days, 30, nameof(Days));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxTransfersPerEvidence, 3, nameof(MaxTransfersPerEvidence));

        var capacity = (long)Evidences * (1 + 2L * MaxTransfersPerEvidence);
        if (TotalEvents < Evidences + OrdinaryPending + 2 || TotalEvents > capacity)
            throw new ArgumentOutOfRangeException(nameof(TotalEvents), TotalEvents,
                $"{Evidences} evidences hold between {Evidences + OrdinaryPending + 2} and {capacity} events with at most {MaxTransfersPerEvidence} transfers each.");

        // Four-digit daily indexes: even a busy day must stay far below 9,999 per type.
        if (Evidences / Days > 2_000)
            throw new ArgumentOutOfRangeException(nameof(Evidences), Evidences, $"Too many evidences for {Days} days; daily indexes would overflow.");
    }
}
