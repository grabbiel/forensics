using EvidenceChain.Domain.Catalog;

namespace EvidenceChain.SyntheticData;

/// <summary>One evidence before content and custody exist: type, timestamps, code and people.</summary>
internal sealed record PlannedEvidence(
    int Order,
    string TypeCode,
    DateTime RegisteredAtUtc,
    DateTime CapturedAtUtc,
    int DailyNo,
    string Code,
    string RegisteredBy,
    string InitialCustodian,
    DeterministicRandom ContentRandom);

/// <summary>Spreads the profile's evidences over the days before the anchor, with one busy day.</summary>
internal static class EvidenceSchedule
{
    public const int BusyDayCount = 44;

    /// <summary>Plans every evidence in registration order; daily numbers restart per (type, UTC date).</summary>
    public static IReadOnlyList<PlannedEvidence> Plan(DeterministicRandom random, DateTime anchorUtc, DatasetProfile profile)
    {
        var days = profile.Days;
        var windowStart = anchorUtc.AddDays(-days);
        var windowMs = (long)days * 24 * 3_600_000;
        var types = profile.TypeSplit.SelectMany(t => Enumerable.Repeat(t.Type, t.Count)).ToList();
        random.Shuffle(types);

        // Busy day: dozens of one type on one whole UTC date, to exercise four-digit daily numbering.
        var busyDate = windowStart.Date.AddDays(random.Next(5, days - 5));
        var busyLeft = BusyDayCount;

        var investigators = SyntheticPeople.WithRole(SyntheticRole.Investigador);
        var custodians = SyntheticPeople.WithRole(SyntheticRole.Custodio);
        var drafts = types.Select((type, order) =>
        {
            var registered = type == EvidenceTypes.Log && busyLeft-- > 0
                ? AtMs(busyDate, random.NextInt64(0, 24 * 3_600_000))
                : AtMs(windowStart, random.NextInt64(0, windowMs));
            var capturedAt = AtMs(registered, -random.NextInt64(0, 72 * 3_600_000 + 1));
            return (Order: order, Type: type, Registered: registered, Captured: capturedAt,
                By: random.Pick(investigators), Custodian: random.Pick(custodians), Random: random.Fork());
        }).ToList();

        var dailyCounters = new Dictionary<(string, DateOnly), int>();
        return drafts
            .OrderBy(d => d.Registered).ThenBy(d => d.Order)
            .Select((d, order) =>
            {
                var date = DateOnly.FromDateTime(d.Registered);
                var dailyNo = dailyCounters[(d.Type, date)] = dailyCounters.GetValueOrDefault((d.Type, date)) + 1;
                return new PlannedEvidence(order, d.Type, d.Registered, d.Captured, dailyNo,
                    EvidenceCode.Format(d.Type, date, dailyNo), d.By, d.Custodian, d.Random);
            })
            .ToArray();
    }

    private static DateTime AtMs(DateTime from, long ms) => DateTime.SpecifyKind(from.AddTicks(ms * TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
}
