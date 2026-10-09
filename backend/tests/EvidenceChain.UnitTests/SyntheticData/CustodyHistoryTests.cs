using EvidenceChain.SyntheticData;

namespace EvidenceChain.UnitTests.SyntheticData;

public sealed class CustodyHistoryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromHours(48);
    private static readonly IReadOnlyDictionary<string, SyntheticRole> Roles = SyntheticPeople.All.ToDictionary(u => u.UserName, u => u.Role);

    [Theory, InlineData("reference"), InlineData("scale")]
    public void People_are_three_investigators_six_custodians_and_two_supervisors_on_example_test(string profile)
    {
        var (data, index) = Load(profile);
        Assert.Equal(3, data.Users.Count(u => u.Role == SyntheticRole.Investigador));
        Assert.Equal(6, data.Users.Count(u => u.Role == SyntheticRole.Custodio));
        Assert.Equal(2, data.Users.Count(u => u.Role == SyntheticRole.Supervisor));
        Assert.All(data.Users, u => Assert.EndsWith("@example.test", u.Email));
        Assert.Contains(data.Users, u => u.Email == "custodio.demo@example.test" && u.Role == SyntheticRole.Custodio);
    }

    [Theory, InlineData("reference"), InlineData("scale")]
    public void The_event_total_is_exact(string profile)
    {
        var (data, _) = Load(profile);
        Assert.Equal(data.Profile.TotalEvents, data.Events.Count);
        Assert.Equal(data.Profile.TotalEvents, data.Evidences.Sum(e => e.EventCount));
        if (profile == "reference")
            Assert.Equal(10_000, data.Events.Count);
    }

    [Theory, InlineData("reference"), InlineData("scale")]
    public void Each_chain_starts_with_registration_and_strictly_increases_before_the_anchor(string profile)
    {
        var (data, index) = Load(profile);
        foreach (var evidence in data.Evidences)
        {
            var chain = index.Chain(evidence.Code);
            Assert.Equal(Enumerable.Range(1, evidence.EventCount), chain.Select(e => e.Seq));
            Assert.Equal(SyntheticEventKind.EvidenceRegistered, chain[0].Kind);
            Assert.Equal(evidence.RegisteredAtUtc, chain[0].OccurredAtUtc);
            Assert.Equal(evidence.RegisteredBy, chain[0].Actor);
            Assert.All(chain.Zip(chain.Skip(1)), pair => Assert.True(pair.Second.OccurredAtUtc > pair.First.OccurredAtUtc, $"{evidence.Code}: time must increase."));
            Assert.True(chain[^1].OccurredAtUtc < data.AnchorUtc);
        }
    }

    [Theory, InlineData("reference"), InlineData("scale")]
    public void Transfers_obey_the_custody_rules(string profile)
    {
        var (data, index) = Load(profile);
        foreach (var evidence in data.Evidences)
        {
            var transfers = index.TransfersOf(evidence.Code);
            Assert.InRange(transfers.Count, 0, 10);

            var custodian = evidence.InitialCustodian;
            Assert.Equal(SyntheticRole.Custodio, Roles[custodian]);
            foreach (var t in transfers)
            {
                Assert.Equal(SyntheticRole.Investigador, Roles[t.RequestedBy]);
                Assert.Equal(custodian, t.FromCustodian);
                Assert.Equal(SyntheticRole.Custodio, Roles[t.ToCustodian]);
                Assert.NotEqual(t.FromCustodian, t.ToCustodian);

                var request = index.Chain(evidence.Code)[t.RequestSeq - 1];
                Assert.Equal((SyntheticEventKind.TransferRequested, t.RequestedBy, t.RequestedAtUtc, (int?)t.Number), (request.Kind, request.Actor, request.OccurredAtUtc, request.TransferNumber));

                if (t.Status == SyntheticTransferStatus.Pending)
                {
                    Assert.Same(transfers[^1], t); // nothing happens after a pending request
                    continue;
                }

                var decision = index.Chain(evidence.Code)[t.RequestSeq];
                var expected = t.Status == SyntheticTransferStatus.Accepted ? SyntheticEventKind.TransferAccepted : SyntheticEventKind.TransferRejected;
                Assert.Equal((expected, t.ToCustodian, t.DecidedAtUtc, (int?)t.Number), (decision.Kind, decision.Actor, (DateTime?)decision.OccurredAtUtc, decision.TransferNumber));
                if (t.Status == SyntheticTransferStatus.Accepted)
                    custodian = t.ToCustodian;
            }

            Assert.Equal(custodian, evidence.CurrentCustodian); // current custodian = fold of accepted transfers
        }
    }

    [Theory, InlineData("reference"), InlineData("scale")]
    public void Decisions_meet_the_48_hour_deadline_except_the_three_accepted_late(string profile)
    {
        var (data, index) = Load(profile);
        var late = data.Transfers.Where(t => t.DecidedAtUtc - t.RequestedAtUtc > Deadline).ToArray();

        Assert.Equal(3, late.Length);
        Assert.All(late, t => Assert.Equal(SyntheticTransferStatus.Accepted, t.Status));
        Assert.Equal(data.Fixtures.AcceptedLate, late.Select(t => t.EvidenceCode).Order(StringComparer.Ordinal));
    }

    [Theory, InlineData("reference"), InlineData("scale")]
    public void Most_transfers_are_accepted_some_rejected_and_a_few_left_pending(string profile)
    {
        var (data, index) = Load(profile);
        var accepted = data.Transfers.Count(t => t.Status == SyntheticTransferStatus.Accepted);
        var rejected = data.Transfers.Count(t => t.Status == SyntheticTransferStatus.Rejected);

        Assert.Equal(data.Profile.OrdinaryPending + 2, data.Transfers.Count(t => t.Status == SyntheticTransferStatus.Pending));
        Assert.InRange(accepted / (double)(accepted + rejected), 0.84, 0.91);
    }

    [Theory, InlineData("reference"), InlineData("scale")]
    public void Idempotency_keys_are_unique_uuid_v7(string profile)
    {
        var (data, index) = Load(profile);
        var keys = data.Transfers.Select(t => t.ClientRequestId).Concat(data.Transfers.Where(t => t.DecisionKey is not null).Select(t => t.DecisionKey!.Value)).ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.All(keys, k => Assert.Equal(7, k.Version));
    }

    [Theory, InlineData("reference"), InlineData("scale")]
    public void Fixtures_are_distinct_and_show_what_they_claim(string profile)
    {
        var (data, index) = Load(profile);
        var f = data.Fixtures;
        string[] codes = [f.Intact, f.OverdueTransfer, f.FreshPending, f.LargeEmail, f.EventTampered, f.ContentTampered, f.CustodianTampered, .. f.AcceptedLate];
        Assert.Equal(codes.Length, codes.Distinct().Count());

        var intact = index.TransfersOf(f.Intact);
        Assert.True(index.Chain(f.Intact).Count >= 6);
        Assert.All(intact, t => Assert.Equal(SyntheticTransferStatus.Accepted, t.Status));

        var overdue = index.TransfersOf(f.OverdueTransfer)[^1];
        Assert.Equal((SyntheticTransferStatus.Pending, data.AnchorUtc.AddHours(-72)), (overdue.Status, overdue.RequestedAtUtc));

        var fresh = index.TransfersOf(f.FreshPending)[^1];
        Assert.Equal((SyntheticTransferStatus.Pending, data.AnchorUtc.AddHours(-1), SyntheticPeople.DemoCustodian), (fresh.Status, fresh.RequestedAtUtc, fresh.ToCustodian));

        // Every other pending transfer is younger than the deadline and avoids the demo inbox.
        Assert.All(data.Transfers.Where(t => t.Status == SyntheticTransferStatus.Pending && t.EvidenceCode != f.OverdueTransfer), t =>
            Assert.True(data.AnchorUtc - t.RequestedAtUtc < Deadline));
        Assert.Single(data.Transfers, t => t.Status == SyntheticTransferStatus.Pending && t.ToCustodian == SyntheticPeople.DemoCustodian);

        Assert.True(index.Chain(f.EventTampered).Count >= f.EventTamperedSeq);
        var tampered = data.Evidences.Single(e => e.Code == f.ContentTampered);
        Assert.True(char.IsAsciiLetter((char)tampered.Content[f.ContentTamperedOffset]));
        var custodianTampered = data.Evidences.Single(e => e.Code == f.CustodianTampered);
        Assert.NotEqual(custodianTampered.CurrentCustodian, f.CustodianTamperedTo);
        Assert.Equal(SyntheticRole.Custodio, Roles[f.CustodianTamperedTo]);
    }

    /// <summary>Chains and transfers indexed by evidence code, once per profile: the rule checks look them up constantly.</summary>
    private sealed record Index(ILookup<string, SyntheticEvent> Events, ILookup<string, SyntheticTransfer> Transfers)
    {
        public IReadOnlyList<SyntheticEvent> Chain(string code) => Events[code].ToArray();
        public IReadOnlyList<SyntheticTransfer> TransfersOf(string code) => Transfers[code].ToArray();
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Index> Indexes = new();

    private static (SyntheticDataset Data, Index Index) Load(string profile)
    {
        var data = Reference.For(profile);
        return (data, Indexes.GetOrAdd(profile, _ => new Index(
            data.Events.OrderBy(e => e.Seq).ToLookup(e => e.EvidenceCode),
            data.Transfers.OrderBy(t => t.RequestSeq).ToLookup(t => t.EvidenceCode))));
    }
}
