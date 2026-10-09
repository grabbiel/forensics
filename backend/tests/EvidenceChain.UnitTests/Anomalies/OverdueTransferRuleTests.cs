using EvidenceChain.Domain.Anomalies;
using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.Custody;
using EvidenceChain.Domain.People;

namespace EvidenceChain.UnitTests.Anomalies;

public sealed class OverdueTransferRuleTests
{
    private static readonly DateTime Requested = new(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Deadline = TimeSpan.FromHours(48);

    [Theory]
    [InlineData(47, null)]
    [InlineData(48, null)] // on the deadline is still on time
    [InlineData(72, AnomalySeverity.Medium)]
    [InlineData(96, AnomalySeverity.High)] // twice the deadline
    public void Pending_transfers_turn_overdue_after_the_deadline(int hoursPending, AnomalySeverity? expected)
    {
        var rule = new OverdueTransferRule(Deadline, new FixedClock(Requested.AddHours(hoursPending)));
        var finding = rule.Evaluate(Pending());

        Assert.Equal(expected, finding?.Severity);
        Assert.Equal(expected is not null, rule.IsSatisfiedBy(Pending()));
        if (finding is not null)
            Assert.Equal(TransferAnomalyKind.Overdue, finding.Kind);
    }

    [Fact]
    public void The_explanation_reads_as_a_sentence()
    {
        var finding = new OverdueTransferRule(Deadline, new FixedClock(Requested.AddHours(72))).Evaluate(Pending())!;
        Assert.Equal("Pendiente desde hace 72 h; el plazo de aceptación es 48 h (24 h de retraso).", finding.Explanation);
    }

    [Fact]
    public void Late_acceptance_is_a_historical_finding_and_rejections_are_not()
    {
        var rule = new OverdueTransferRule(Deadline, new FixedClock(Requested.AddDays(30)));

        var late = rule.Evaluate(Decided(accept: true, afterHours: 60))!;
        Assert.Equal((TransferAnomalyKind.AcceptedLate, AnomalySeverity.Medium), (late.Kind, late.Severity));
        Assert.Equal("Aceptada 60 h después de solicitarse; el plazo es 48 h (12 h de retraso).", late.Explanation);

        Assert.Null(rule.Evaluate(Decided(accept: true, afterHours: 20)));
        Assert.Null(rule.Evaluate(Decided(accept: false, afterHours: 200)));
    }

    [Fact]
    public void The_deadline_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverdueTransferRule(TimeSpan.Zero, TimeProvider.System));
    }

    private static (Evidence Evidence, CustodyTransfer Transfer) Requested4To5()
    {
        var evidence = new Evidence(EvidenceTypes.Csv, DateOnly.FromDateTime(Requested), 1, "Extracto", Requested, Requested, 1, 4, new EvidenceContent([1], "text/csv"));
        var transfer = CustodyTransfer.Request(evidence, new Actor(1, UserRole.Investigador), new Actor(5, UserRole.Custodio), null, Requested, "Análisis", Guid.CreateVersion7(), new byte[32]);
        return (evidence, transfer);
    }

    private static CustodyTransfer Pending() => Requested4To5().Transfer;

    private static CustodyTransfer Decided(bool accept, int afterHours)
    {
        var (evidence, transfer) = Requested4To5();
        var at = Requested.AddHours(afterHours);
        if (accept)
            transfer.Accept(evidence, new Actor(5, UserRole.Custodio), at, null, Guid.CreateVersion7(), new byte[32]);
        else
            transfer.Reject(evidence, new Actor(5, UserRole.Custodio), at, "Sin orden", Guid.CreateVersion7(), new byte[32]);
        return transfer;
    }

    private sealed class FixedClock(DateTime nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(nowUtc, TimeSpan.Zero);
    }
}
