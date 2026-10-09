using EvidenceChain.SyntheticData;

namespace EvidenceChain.UnitTests.SyntheticData;

public sealed class CustodyHistoryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromHours(48);
    private static SyntheticDataset Data => Reference.Dataset;
    private static readonly IReadOnlyDictionary<string, SyntheticRole> Roles = SyntheticPeople.All.ToDictionary(u => u.UserName, u => u.Role);

    [Fact]
    public void People_are_three_investigators_six_custodians_and_two_supervisors_on_example_test()
    {
        Assert.Equal(3, Data.Users.Count(u => u.Role == SyntheticRole.Investigador));
        Assert.Equal(6, Data.Users.Count(u => u.Role == SyntheticRole.Custodio));
        Assert.Equal(2, Data.Users.Count(u => u.Role == SyntheticRole.Supervisor));
        Assert.All(Data.Users, u => Assert.EndsWith("@example.test", u.Email));
        Assert.Contains(Data.Users, u => u.Email == "custodio.demo@example.test" && u.Role == SyntheticRole.Custodio);
    }

    [Fact]
    public void There_are_exactly_10000_events()
    {
        Assert.Equal(10_000, Data.Events.Count);
        Assert.Equal(10_000, Data.Evidences.Sum(e => e.EventCount));
    }

    [Fact]
    public void Each_chain_starts_with_registration_and_strictly_increases_before_the_anchor()
    {
        foreach (var evidence in Data.Evidences)
        {
            var chain = Chain(evidence.Code);
            Assert.Equal(Enumerable.Range(1, evidence.EventCount), chain.Select(e => e.Seq));
            Assert.Equal(SyntheticEventKind.EvidenceRegistered, chain[0].Kind);
            Assert.Equal(evidence.RegisteredAtUtc, chain[0].OccurredAtUtc);
            Assert.Equal(evidence.RegisteredBy, chain[0].Actor);
            Assert.All(chain.Zip(chain.Skip(1)), pair => Assert.True(pair.Second.OccurredAtUtc > pair.First.OccurredAtUtc, $"{evidence.Code}: time must increase."));
            Assert.True(chain[^1].OccurredAtUtc < Data.AnchorUtc);
        }
    }

    [Fact]
    public void Transfers_obey_the_custody_rules()
    {
        foreach (var evidence in Data.Evidences)
        {
            var transfers = Transfers(evidence.Code);
            Assert.InRange(transfers.Count, 0, 10);

            var custodian = evidence.InitialCustodian;
            Assert.Equal(SyntheticRole.Custodio, Roles[custodian]);
            foreach (var t in transfers)
            {
                Assert.Equal(SyntheticRole.Investigador, Roles[t.RequestedBy]);
                Assert.Equal(custodian, t.FromCustodian);
                Assert.Equal(SyntheticRole.Custodio, Roles[t.ToCustodian]);
                Assert.NotEqual(t.FromCustodian, t.ToCustodian);

                var request = Chain(evidence.Code)[t.RequestSeq - 1];
                Assert.Equal((SyntheticEventKind.TransferRequested, t.RequestedBy, t.RequestedAtUtc, (int?)t.Number), (request.Kind, request.Actor, request.OccurredAtUtc, request.TransferNumber));

                if (t.Status == SyntheticTransferStatus.Pending)
                {
                    Assert.Same(transfers[^1], t); // nothing happens after a pending request
                    continue;
                }

                var decision = Chain(evidence.Code)[t.RequestSeq];
                var expected = t.Status == SyntheticTransferStatus.Accepted ? SyntheticEventKind.TransferAccepted : SyntheticEventKind.TransferRejected;
                Assert.Equal((expected, t.ToCustodian, t.DecidedAtUtc, (int?)t.Number), (decision.Kind, decision.Actor, (DateTime?)decision.OccurredAtUtc, decision.TransferNumber));
                if (t.Status == SyntheticTransferStatus.Accepted)
                    custodian = t.ToCustodian;
            }

            Assert.Equal(custodian, evidence.CurrentCustodian); // current custodian = fold of accepted transfers
        }
    }

    [Fact]
    public void Decisions_meet_the_48_hour_deadline_except_the_three_accepted_late()
    {
        var late = Data.Transfers.Where(t => t.DecidedAtUtc - t.RequestedAtUtc > Deadline).ToArray();

        Assert.Equal(3, late.Length);
        Assert.All(late, t => Assert.Equal(SyntheticTransferStatus.Accepted, t.Status));
        Assert.Equal(Data.Fixtures.AcceptedLate, late.Select(t => t.EvidenceCode).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Most_transfers_are_accepted_some_rejected_and_four_left_pending()
    {
        var accepted = Data.Transfers.Count(t => t.Status == SyntheticTransferStatus.Accepted);
        var rejected = Data.Transfers.Count(t => t.Status == SyntheticTransferStatus.Rejected);

        Assert.Equal(4, Data.Transfers.Count(t => t.Status == SyntheticTransferStatus.Pending));
        Assert.InRange(accepted / (double)(accepted + rejected), 0.84, 0.91);
    }

    [Fact]
    public void Idempotency_keys_are_unique_uuid_v7()
    {
        var keys = Data.Transfers.Select(t => t.ClientRequestId).Concat(Data.Transfers.Where(t => t.DecisionKey is not null).Select(t => t.DecisionKey!.Value)).ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.All(keys, k => Assert.Equal(7, k.Version));
    }

    [Fact]
    public void Fixtures_are_distinct_and_show_what_they_claim()
    {
        var f = Data.Fixtures;
        string[] codes = [f.Intact, f.OverdueTransfer, f.FreshPending, f.LargeEmail, f.EventTampered, f.ContentTampered, f.CustodianTampered, .. f.AcceptedLate];
        Assert.Equal(codes.Length, codes.Distinct().Count());

        var intact = Transfers(f.Intact);
        Assert.True(Chain(f.Intact).Count >= 6);
        Assert.All(intact, t => Assert.Equal(SyntheticTransferStatus.Accepted, t.Status));

        var overdue = Transfers(f.OverdueTransfer)[^1];
        Assert.Equal((SyntheticTransferStatus.Pending, Data.AnchorUtc.AddHours(-72)), (overdue.Status, overdue.RequestedAtUtc));

        var fresh = Transfers(f.FreshPending)[^1];
        Assert.Equal((SyntheticTransferStatus.Pending, Data.AnchorUtc.AddHours(-1), SyntheticPeople.DemoCustodian), (fresh.Status, fresh.RequestedAtUtc, fresh.ToCustodian));

        // Every other pending transfer is younger than the deadline and avoids the demo inbox.
        Assert.All(Data.Transfers.Where(t => t.Status == SyntheticTransferStatus.Pending && t.EvidenceCode != f.OverdueTransfer), t =>
            Assert.True(Data.AnchorUtc - t.RequestedAtUtc < Deadline));
        Assert.Single(Data.Transfers, t => t.Status == SyntheticTransferStatus.Pending && t.ToCustodian == SyntheticPeople.DemoCustodian);

        Assert.True(Chain(f.EventTampered).Count >= f.EventTamperedSeq);
        var tampered = Data.Evidences.Single(e => e.Code == f.ContentTampered);
        Assert.True(char.IsAsciiLetter((char)tampered.Content[f.ContentTamperedOffset]));
        var custodianTampered = Data.Evidences.Single(e => e.Code == f.CustodianTampered);
        Assert.NotEqual(custodianTampered.CurrentCustodian, f.CustodianTamperedTo);
        Assert.Equal(SyntheticRole.Custodio, Roles[f.CustodianTamperedTo]);
    }

    // Indexed once: the rule checks look chains up thousands of times.
    private static readonly Lazy<ILookup<string, SyntheticEvent>> EventsByCode = new(() => Data.Events.OrderBy(e => e.Seq).ToLookup(e => e.EvidenceCode));
    private static readonly Lazy<ILookup<string, SyntheticTransfer>> TransfersByCode = new(() => Data.Transfers.OrderBy(t => t.RequestSeq).ToLookup(t => t.EvidenceCode));

    private static IReadOnlyList<SyntheticEvent> Chain(string code) => EventsByCode.Value[code].ToArray();

    private static IReadOnlyList<SyntheticTransfer> Transfers(string code) => TransfersByCode.Value[code].ToArray();
}
