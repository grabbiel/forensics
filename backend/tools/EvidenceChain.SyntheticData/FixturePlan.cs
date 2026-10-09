using EvidenceChain.Domain.Catalog;

namespace EvidenceChain.SyntheticData;

/// <summary>Which planned evidences carry each named fixture; all distinct, all at least 20 days old.</summary>
internal sealed record FixturePlan(
    PlannedEvidence Intact,
    PlannedEvidence OverdueTransfer,
    PlannedEvidence FreshPending,
    PlannedEvidence LargeEmail,
    PlannedEvidence EventTampered,
    PlannedEvidence ContentTampered,
    PlannedEvidence CustodianTampered,
    IReadOnlyList<PlannedEvidence> AcceptedLate,
    IReadOnlyList<PlannedEvidence> OrdinaryPending)
{
    public const int AcceptedLateCount = 3;

    /// <summary>Chooses fixtures deterministically from evidences old enough to hold long histories.</summary>
    public static FixturePlan Choose(IReadOnlyList<PlannedEvidence> planned, DateTime anchorUtc, int ordinaryPending, DeterministicRandom random)
    {
        var pool = planned.Where(p => p.RegisteredAtUtc <= anchorUtc.AddDays(-20)).ToList();
        random.Shuffle(pool);

        PlannedEvidence Take(Func<PlannedEvidence, bool> eligible)
        {
            var chosen = pool.First(eligible);
            pool.Remove(chosen);
            return chosen;
        }

        return new FixturePlan(
            Intact: Take(_ => true),
            OverdueTransfer: Take(_ => true),
            // The demo Custodio must not already hold it, or the transfer to them would be invalid.
            FreshPending: Take(p => p.InitialCustodian != SyntheticPeople.DemoCustodian),
            LargeEmail: Take(p => p.TypeCode == EvidenceTypes.Eml),
            EventTampered: Take(_ => true),
            ContentTampered: Take(_ => true),
            CustodianTampered: Take(_ => true),
            AcceptedLate: Enumerable.Range(0, AcceptedLateCount).Select(_ => Take(_ => true)).ToArray(),
            OrdinaryPending: Enumerable.Range(0, ordinaryPending).Select(_ => Take(_ => true)).ToArray());
    }

    /// <summary>Fixture name per evidence code, as shown in the manifest.</summary>
    public IReadOnlyDictionary<string, string> NamesByCode()
    {
        var names = new Dictionary<string, string>
        {
            [Intact.Code] = "intact",
            [OverdueTransfer.Code] = "overdue-transfer",
            [FreshPending.Code] = "fresh-pending",
            [LargeEmail.Code] = "large-email",
            [EventTampered.Code] = "event-tampered",
            [ContentTampered.Code] = "content-tampered",
            [CustodianTampered.Code] = "custodian-tampered",
        };
        foreach (var late in AcceptedLate)
            names[late.Code] = "accepted-late";
        return names;
    }
}
