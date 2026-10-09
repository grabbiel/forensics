namespace EvidenceChain.SyntheticData;

/// <summary>Custody histories that obey the domain rules, totalling exactly the profile's event count.</summary>
internal static class CustodySimulator
{
    public const double AcceptRate = 0.875;

    /// <summary>Acceptance deadline, matching the API's Anomalies:TransferAcceptanceDeadline default.</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromHours(48);

    // Timing uses whole milliseconds only, so byte identity never rests on floating-point rounding.
    private const long Minute = 60_000;
    private const long Hour = 60 * Minute;
    private const long MinSlotMs = 4 * Minute;
    private const long LateBudgetMs = 73 * Hour;

    private static readonly string[] Reasons =
    [
        "Análisis en el laboratorio forense", "Copia para peritaje judicial", "Revisión por auditoría interna",
        "Custodia temporal durante el traslado", "Contraste con otras evidencias del caso",
    ];

    private static readonly string[] RejectionReasons =
    [
        "Solicitud sin orden judicial adjunta", "Destinatario sin capacidad de almacenamiento", "Destinatario incorrecto para este caso",
    ];

    public sealed record Result(
        IReadOnlyList<SyntheticTransfer> Transfers,
        IReadOnlyList<SyntheticEvent> Events,
        IReadOnlyDictionary<string, string> CurrentCustodian,
        IReadOnlyDictionary<string, int> EventCount);

    /// <summary>Simulates every evidence in registration order.</summary>
    public static Result Simulate(IReadOnlyList<PlannedEvidence> evidences, FixturePlan fixtures, DatasetProfile profile, DateTime anchorUtc, DeterministicRandom random)
    {
        var pendingAt = new Dictionary<string, DateTime>
        {
            [fixtures.OverdueTransfer.Code] = anchorUtc - TimeSpan.FromHours(72),
            [fixtures.FreshPending.Code] = anchorUtc - TimeSpan.FromHours(1),
        };
        foreach (var ordinary in fixtures.OrdinaryPending)
            pendingAt[ordinary.Code] = At(anchorUtc, -random.NextInt64(2 * Hour, 30 * Hour)); // never overdue

        var late = fixtures.AcceptedLate.Select(e => e.Code).ToHashSet();
        var minimum = new Dictionary<string, int> { [fixtures.Intact.Code] = 3, [fixtures.EventTampered.Code] = 2, [fixtures.CustodianTampered.Code] = 1 };
        foreach (var code in late)
            minimum[code] = 1;

        // A decided transfer adds two events, a pending one adds one.
        var decidedTotal = Math.DivRem(profile.TotalEvents - evidences.Count - pendingAt.Count, 2, out var odd);
        if (odd != 0)
            throw new InvalidOperationException("Pending transfers must be even for the event total to be reachable.");

        var plans = evidences.Select(e =>
        {
            var start = e.RegisteredAtUtc + TimeSpan.FromMinutes(1);
            var end = (pendingAt.TryGetValue(e.Code, out var p) ? p : anchorUtc) - TimeSpan.FromMinutes(1);
            var usableMs = Ms(end - start) - (late.Contains(e.Code) ? LateBudgetMs : 0);
            var max = (int)Math.Clamp(usableMs / MinSlotMs, 0, profile.MaxTransfersPerEvidence - (pendingAt.ContainsKey(e.Code) ? 1 : 0));
            var min = Math.Min(minimum.GetValueOrDefault(e.Code), max);
            var drawn = random.Next(0, (int)Math.Clamp((end - start).Ticks / TimeSpan.TicksPerDay, 0, 9) + 1);
            return new EvidencePlan(e, start, end, min, max, Math.Clamp(drawn, min, max));
        }).ToArray();

        Rebalance(plans, decidedTotal);

        var transfers = new List<SyntheticTransfer>();
        var events = new List<SyntheticEvent>();
        var current = new Dictionary<string, string>();
        var counts = new Dictionary<string, int>();
        foreach (var plan in plans)
        {
            var evidence = plan.Evidence;
            var history = new History(evidence, events, transfers, random);
            history.Add(SyntheticEventKind.EvidenceRegistered, evidence.RegisteredAtUtc, evidence.RegisteredBy, null, "Registro inicial de la evidencia");

            var custodian = evidence.InitialCustodian;
            var cursor = plan.Start;
            var remaining = plan.Decided;
            var avoidDemo = evidence == fixtures.FreshPending;

            if (late.Contains(evidence.Code))
            {
                var requested = At(cursor, random.NextInt64(1 * Minute, 61 * Minute));
                var decided = At(requested, random.NextInt64(50 * Hour, 70 * Hour));
                custodian = history.Transfer(custodian, requested, decided, accept: true, avoidDemo);
                cursor = decided + TimeSpan.FromMinutes(1);
                remaining--;
            }

            if (remaining > 0)
            {
                // Equal slots: request in the first fifth, decide within the next three quarters, so slots never overlap.
                var slotMs = Ms(plan.End - cursor) / remaining;
                var maxDelayMs = Math.Min(47 * Hour, slotMs * 3 / 4);
                for (var k = 0; k < remaining; k++)
                {
                    var requested = At(cursor, slotMs * k + random.NextInt64(0, slotMs / 5 + 1));
                    var decided = At(requested, random.NextInt64(maxDelayMs / 20, maxDelayMs) + 1);
                    var accept = evidence == fixtures.Intact || random.NextBool(AcceptRate);
                    custodian = history.Transfer(custodian, requested, decided, accept, avoidDemo);
                }
            }

            if (pendingAt.TryGetValue(evidence.Code, out var pendingRequestedAt))
            {
                var recipient = evidence == fixtures.FreshPending ? SyntheticPeople.DemoCustodian : null;
                history.Pending(custodian, pendingRequestedAt, recipient);
            }

            current[evidence.Code] = custodian;
            counts[evidence.Code] = history.Count;
        }

        return new Result(transfers, events, current, counts);
    }

    /// <summary>Moves the decided-transfer total to <paramref name="target"/>, newest evidence first, within each plan's bounds.</summary>
    private static void Rebalance(EvidencePlan[] plans, int target)
    {
        var diff = target - plans.Sum(p => p.Decided);
        while (diff != 0)
        {
            var moved = false;
            for (var i = plans.Length - 1; i >= 0 && diff != 0; i--)
            {
                var p = plans[i];
                if (diff > 0 && p.Decided < p.Max) { p.Decided++; diff--; moved = true; }
                else if (diff < 0 && p.Decided > p.Min) { p.Decided--; diff++; moved = true; }
            }
            if (!moved)
                throw new ArgumentException(
                    "The event total cannot be reached: recent evidences have too little time for that many transfers. Lower --events or raise --evidences or --max-transfers.");
        }
    }

    private static long Ms(TimeSpan span) => span.Ticks / TimeSpan.TicksPerMillisecond;

    private static DateTime At(DateTime from, long ms) => from.AddTicks(ms * TimeSpan.TicksPerMillisecond);

    private sealed class EvidencePlan(PlannedEvidence evidence, DateTime start, DateTime end, int min, int max, int decided)
    {
        public PlannedEvidence Evidence { get; } = evidence;
        public DateTime Start { get; } = start;
        public DateTime End { get; } = end;
        public int Min { get; } = min;
        public int Max { get; } = max;
        public int Decided { get; set; } = decided;
    }

    /// <summary>Appends events and transfers for one evidence, numbering them as it goes.</summary>
    private sealed class History(PlannedEvidence evidence, List<SyntheticEvent> events, List<SyntheticTransfer> transfers, DeterministicRandom random)
    {
        private static readonly IReadOnlyList<string> Investigators = SyntheticPeople.WithRole(SyntheticRole.Investigador);
        private static readonly IReadOnlyList<string> Custodians = SyntheticPeople.WithRole(SyntheticRole.Custodio);

        public int Count { get; private set; }

        public void Add(SyntheticEventKind kind, DateTime at, string actor, int? transfer, string notes) =>
            events.Add(new SyntheticEvent(evidence.Code, ++Count, kind, at, actor, transfer, notes));

        /// <summary>A decided transfer; returns the custodian afterwards.</summary>
        public string Transfer(string from, DateTime requestedAt, DateTime decidedAt, bool accept, bool avoidDemo)
        {
            var to = Recipient(from, avoidDemo);
            var number = transfers.Count + 1;
            var requestedBy = random.Pick(Investigators);
            var reason = random.Pick(Reasons);
            var requestSeq = Count + 1;
            Add(SyntheticEventKind.TransferRequested, requestedAt, requestedBy, number, reason);

            var notes = accept ? "Recibida y verificada" : random.Pick(RejectionReasons);
            Add(accept ? SyntheticEventKind.TransferAccepted : SyntheticEventKind.TransferRejected, decidedAt, to, number, notes);
            transfers.Add(new SyntheticTransfer(number, evidence.Code, requestSeq, from, to, requestedBy, requestedAt, reason,
                random.NextUuidV7(requestedAt), accept ? SyntheticTransferStatus.Accepted : SyntheticTransferStatus.Rejected,
                decidedAt, notes, random.NextUuidV7(decidedAt)));
            return accept ? to : from;
        }

        /// <summary>A transfer still awaiting its recipient; always the evidence's last.</summary>
        public void Pending(string from, DateTime requestedAt, string? recipient)
        {
            var to = recipient ?? Recipient(from, avoidDemo: true);
            var number = transfers.Count + 1;
            var requestedBy = random.Pick(Investigators);
            var reason = random.Pick(Reasons);
            var requestSeq = Count + 1;
            Add(SyntheticEventKind.TransferRequested, requestedAt, requestedBy, number, reason);
            transfers.Add(new SyntheticTransfer(number, evidence.Code, requestSeq, from, to, requestedBy, requestedAt, reason,
                random.NextUuidV7(requestedAt), SyntheticTransferStatus.Pending, null, null, null));
        }

        private string Recipient(string from, bool avoidDemo)
        {
            while (true)
            {
                var candidate = random.Pick(Custodians);
                if (candidate != from && !(avoidDemo && candidate == SyntheticPeople.DemoCustodian))
                    return candidate;
            }
        }
    }
}
