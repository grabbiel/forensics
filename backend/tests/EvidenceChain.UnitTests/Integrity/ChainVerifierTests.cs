using System.Reflection;
using System.Security.Cryptography;
using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.Custody;
using EvidenceChain.Domain.Integrity;
using EvidenceChain.Domain.People;
using static EvidenceChain.Domain.Custody.CustodyEventKind;

namespace EvidenceChain.UnitTests.Integrity;

public sealed class ChainVerifierTests
{
    private static readonly IntegrityKeyRing Keys = new("k1", new Dictionary<string, byte[]>
    {
        ["k1"] = SHA256.HashData("verifier-tests"u8.ToArray()),
        ["k2"] = SHA256.HashData("other"u8.ToArray()),
    });
    private static readonly DateTime Registered = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Content = "fichero de prueba\n"u8.ToArray();

    [Fact]
    public void An_untouched_chain_is_valid_through_its_last_event()
    {
        var verdict = Verify(Build());

        Assert.True(verdict.IsValid);
        Assert.Equal(6, verdict.VerifiedThroughSeq);
    }

    /// <summary>Every canonical field of every event, changed after signing.</summary>
    public static TheoryData<int, string> Alterations()
    {
        var data = new TheoryData<int, string>();
        foreach (var field in new[] { "OccurredAt", "Actor", "To", "Notes", "Mac", "KeyId" })
        foreach (var seq in Enumerable.Range(1, 6))
            data.Add(seq, field);
        foreach (var field in new[] { "ContentSha256", "ContentLength", "MediaType" })
            data.Add(1, field);
        foreach (var field in new[] { "Seq", "Kind", "From", "PrevMac" })
        foreach (var seq in Enumerable.Range(2, 5))
            data.Add(seq, field);
        return data;
    }

    [Theory, MemberData(nameof(Alterations))]
    public void Altering_any_field_of_event_k_is_reported_at_k(int k, string field)
    {
        var signed = Build();
        signed.Chain[k - 1] = Alter(signed.Chain[k - 1], field);

        var verdict = Verify(signed);

        Assert.False(verdict.IsValid);
        Assert.Equal(k, verdict.FailedAtSeq);
        Assert.Equal(k - 1, verdict.VerifiedThroughSeq);
    }

    [Fact]
    public void Each_failure_has_its_own_code()
    {
        var signed = Build();
        var wrongKey = new IntegrityKeyRing("k1", new Dictionary<string, byte[]> { ["k1"] = new byte[32] });
        var noKey = new IntegrityKeyRing("k2", new Dictionary<string, byte[]> { ["k2"] = new byte[32] });

        Assert.Equal(("MAC_MISMATCH", 1), Code(Verify(signed, keys: wrongKey)));
        Assert.Equal(("UNKNOWN_KEY", 1), Code(Verify(signed, keys: noKey)));
        Assert.Equal(("SEQUENCE_GAP", 3), Code(Verify(signed with { Chain = [.. signed.Chain.Take(2), .. signed.Chain.Skip(3)] })));
        Assert.Equal(("SEQUENCE_GAP", 1), Code(Verify(signed with { Chain = [] })));
        Assert.Equal(("HEAD_MISMATCH", 5), Code(Verify(signed with { Chain = signed.Chain.Take(5).ToList() })));
        Assert.Equal(("CONTENT_HASH_MISMATCH", 1), Code(Verify(signed, content: new EvidenceContent("fichero de pruebA\n"u8.ToArray(), "text/plain"))));
        Assert.Equal(("TRANSITION_VIOLATION", 2), Code(Violation((TransferAccepted, 5, 1, 4, 5))));

        signed.Evidence.HandOverTo(9); // a direct UPDATE of the current custodian
        Assert.Equal(("CUSTODY_PROJECTION_MISMATCH", 6), Code(Verify(signed)));
    }

    [Theory]
    [InlineData("InitialCustodian", 1)]
    [InlineData("RegisteredBy", 1)]
    [InlineData("RegisteredAt", 1)]
    [InlineData("CurrentCustodian", 6)]
    [InlineData("Accepted.RequestedAt", 2)]
    [InlineData("Accepted.RequestedBy", 2)]
    [InlineData("Accepted.Reason", 2)]
    [InlineData("Accepted.Duplicated", 2)]
    [InlineData("Accepted.To", 2)]
    [InlineData("Accepted.MarkedRejected", 3)] // would hide a late acceptance from the overdue rule
    [InlineData("Accepted.DecidedBy", 3)]
    [InlineData("Rejected.From", 4)]
    [InlineData("Rejected.MarkedAccepted", 5)]
    [InlineData("Rejected.DecidedAt", 5)]
    [InlineData("Rejected.DecisionNotes", 5)]
    [InlineData("Pending.MarkedAccepted", 6)]
    [InlineData("Rejected.RowMissing", 4)]
    [InlineData("ExtraRow", 6)]
    [InlineData("RowOfAnotherEvidence", 6)]
    public void A_row_updated_directly_disagrees_with_the_signed_events(string tamper, int seq)
    {
        var signed = Build();
        var (accepted, rejected, pending) = (signed.Transfers[0], signed.Transfers[1], signed.Transfers[2]);
        switch (tamper)
        {
            case "InitialCustodian": Set(signed.Evidence, nameof(Evidence.InitialCustodianId), 9); break;
            case "RegisteredBy": Set(signed.Evidence, nameof(Evidence.RegisteredById), 2); break;
            case "RegisteredAt": Set(signed.Evidence, nameof(Evidence.RegisteredAtUtc), Registered.AddSeconds(1)); break;
            case "CurrentCustodian": signed.Evidence.HandOverTo(9); break;
            case "Accepted.RequestedAt": Set(accepted, nameof(CustodyTransfer.RequestedAtUtc), accepted.RequestedAtUtc.AddSeconds(-1)); break;
            case "Accepted.RequestedBy": Set(accepted, nameof(CustodyTransfer.RequestedById), 3); break;
            case "Accepted.Reason": Set(accepted, nameof(CustodyTransfer.Reason), "Otro motivo"); break;
            case "Accepted.Duplicated": signed.Transfers.Add(WithId(Request(signed.Evidence, requester: 3, to: 4), 1)); break;
            case "Accepted.To": Set(accepted, nameof(CustodyTransfer.ToCustodianId), 9); break;
            case "Accepted.MarkedRejected": Set(accepted, nameof(CustodyTransfer.Status), TransferStatus.Rejected); break;
            case "Accepted.DecidedBy": Set(accepted, nameof(CustodyTransfer.DecidedById), 9); break;
            case "Rejected.From": Set(rejected, nameof(CustodyTransfer.FromCustodianId), 9); break;
            case "Rejected.MarkedAccepted": Set(rejected, nameof(CustodyTransfer.Status), TransferStatus.Accepted); break;
            case "Rejected.DecidedAt": Set(rejected, nameof(CustodyTransfer.DecidedAtUtc), rejected.DecidedAtUtc!.Value.AddMinutes(-1)); break;
            case "Rejected.DecisionNotes": Set(rejected, nameof(CustodyTransfer.DecisionNotes), "Aceptada"); break;
            case "Pending.MarkedAccepted": Set(pending, nameof(CustodyTransfer.Status), TransferStatus.Accepted); break;
            case "Rejected.RowMissing": signed.Transfers.Remove(rejected); break;
            case "ExtraRow": signed.Transfers.Add(WithId(Request(signed.Evidence, requester: 3, to: 4), 4)); break;
            case "RowOfAnotherEvidence": Set(pending, nameof(CustodyTransfer.EvidenceId), 99L); break;
        }

        Assert.Equal(("CUSTODY_PROJECTION_MISMATCH", seq), Code(Verify(signed)));
    }

    [Theory]
    [InlineData("Sha256")]
    [InlineData("ByteLength")]
    [InlineData("MediaType")]
    public void Stored_content_metadata_must_match_the_commitment(string field)
    {
        var signed = Build();
        var content = signed.Evidence.Content;
        switch (field)
        {
            case "Sha256": typeof(EvidenceContent).GetField("_sha256", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(content, new byte[32]); break;
            case "ByteLength": Set(content, nameof(EvidenceContent.ByteLength), Content.Length + 1); break;
            case "MediaType": Set(content, nameof(EvidenceContent.MediaType), "text/csv"); break;
        }

        Assert.Equal(("CONTENT_HASH_MISMATCH", 1), Code(Verify(signed)));
    }

    [Fact]
    public void The_earliest_failure_is_reported_and_the_signed_chain_comes_before_the_rows()
    {
        // A forbidden step at 2 before an edited event at 3.
        var violation = SignedSteps((TransferAccepted, 5, 1, 4, 5), (TransferRequested, 1, 2, 5, 6));
        violation.Chain[2] = Alter(violation.Chain[2], "Notes");
        Assert.Equal(("TRANSITION_VIOLATION", 2), Code(Verify(violation)));

        // Replaced content (seq 1) before an edited event at 3.
        var content = Build();
        content.Chain[2] = Alter(content.Chain[2], "Notes");
        Assert.Equal(("CONTENT_HASH_MISMATCH", 1), Code(Verify(content, content: new EvidenceContent("fichero de pruebA\n"u8.ToArray(), "text/plain"))));

        // Among the rows, an edited request at 2 before an orphan row at the end.
        var rows = Build();
        rows.Transfers.Add(WithId(Request(rows.Evidence, requester: 3, to: 4), 4));
        Set(rows.Transfers[0], nameof(CustodyTransfer.RequestedById), 3);
        Assert.Equal(("CUSTODY_PROJECTION_MISMATCH", 2), Code(Verify(rows)));

        // An edited event outranks an edited row, even an earlier one.
        var both = Build();
        Set(both.Evidence, nameof(Evidence.InitialCustodianId), 9);
        both.Chain[3] = Alter(both.Chain[3], "Notes");
        Assert.Equal(("MAC_MISMATCH", 4), Code(Verify(both)));
    }

    [Fact]
    public void A_signed_history_the_state_machine_forbids_is_a_transition_violation()
    {
        // (kind, actor, transfer, from, to) after registration to custodian 4; the last step is the offending one.
        Assert.Equal(2, ViolationAt((TransferAccepted, 5, 1, 4, 5))); // decided without a request
        Assert.Equal(2, ViolationAt((TransferRequested, 1, 1, 7, 5))); // requested away from someone who does not hold it
        Assert.Equal(2, ViolationAt((TransferRequested, 1, 1, 4, 4))); // sent to its own holder
        Assert.Equal(2, ViolationAt((TransferRequested, 5, 1, 4, 5))); // requested by its own recipient
        Assert.Equal(3, ViolationAt((TransferRequested, 1, 1, 4, 5), (TransferRequested, 1, 2, 4, 6))); // a second open request
        Assert.Equal(3, ViolationAt((TransferRequested, 1, 1, 4, 5), (TransferAccepted, 6, 1, 4, 5))); // decided by someone else
        Assert.Equal(3, ViolationAt((TransferRequested, 1, 1, 4, 5), (TransferAccepted, 5, 2, 4, 5))); // decides another transfer
        Assert.Equal(4, ViolationAt((TransferRequested, 1, 1, 4, 5), (TransferRejected, 5, 1, 4, 5), (TransferAccepted, 5, 1, 4, 5))); // decided twice
        Assert.Equal(4, ViolationAt((TransferRequested, 1, 1, 4, 5), (TransferRejected, 5, 1, 4, 5), (TransferRequested, 1, 1, 4, 5))); // transfer reopened
    }

    private static ChainVerdict Verify(Signed s, IntegrityKeyRing? keys = null, EvidenceContent? content = null) =>
        ChainVerifier.Verify(keys ?? Keys, s.Evidence, s.Chain, s.Transfers, content ?? s.Evidence.Content);

    private static (string, int) Code(ChainVerdict verdict) => (verdict.Failure!.Value.Code(), verdict.FailedAtSeq!.Value);

    private sealed record Signed(Evidence Evidence, List<CustodyEvent> Chain, List<CustodyTransfer> Transfers);

    private static Evidence NewEvidence() =>
        new(EvidenceTypes.Log, DateOnly.FromDateTime(Registered), 1, "Log", Registered.AddHours(-1), Registered, 1, 4, new EvidenceContent(Content, "text/plain"));

    /// <summary>
    /// Registered by user 1 to custodian 4, then through the domain: 4 → 5 accepted (seq 2–3),
    /// 5 → 6 rejected (4–5) and 5 → 7 still pending (6). Custody ends with 5. Event notes carry each
    /// transfer's reason and decision notes, as the API writes them.
    /// </summary>
    private static Signed Build()
    {
        var evidence = NewEvidence();
        var signer = new Signer(evidence);
        signer.Append(EvidenceRegistered, 1, null, null, 4);

        var accepted = Requested(signer, 1, requester: 1, to: 5);
        accepted.Accept(evidence, new Actor(5, UserRole.Custodio), signer.NextAt, null, Guid.CreateVersion7(), new byte[32]);
        signer.Append(TransferAccepted, 5, 1, 4, 5, notes: "");

        var rejected = Requested(signer, 2, requester: 2, to: 6);
        rejected.Reject(evidence, new Actor(6, UserRole.Custodio), signer.NextAt, "Sin orden", Guid.CreateVersion7(), new byte[32]);
        signer.Append(TransferRejected, 6, 2, 5, 6, notes: "Sin orden");

        var pending = Requested(signer, 3, requester: 3, to: 7);
        return new Signed(evidence, signer.Chain, [accepted, rejected, pending]);
    }

    private static CustodyTransfer Requested(Signer signer, long id, int requester, int to)
    {
        var from = signer.Evidence.CurrentCustodianId;
        var transfer = WithId(Request(signer.Evidence, requester, to, signer.NextAt), id);
        signer.Append(TransferRequested, requester, id, from, to, notes: transfer.Reason);
        return transfer;
    }

    private static CustodyTransfer Request(Evidence evidence, int requester, int to, DateTime? at = null) =>
        CustodyTransfer.Request(evidence, new Actor(requester, UserRole.Investigador), new Actor(to, UserRole.Custodio), null,
            at ?? Registered.AddDays(1), "Análisis", Guid.CreateVersion7(), new byte[32]);

    /// <summary>The identity the database would assign.</summary>
    private static CustodyTransfer WithId(CustodyTransfer transfer, long id)
    {
        Set(transfer, nameof(CustodyTransfer.TransferId), id);
        return transfer;
    }

    private static int ViolationAt(params (CustodyEventKind Kind, int Actor, long Transfer, int From, int To)[] steps) => Violation(steps).FailedAtSeq!.Value;

    /// <summary>A correctly signed chain whose steps break the transfer rules.</summary>
    private static ChainVerdict Violation(params (CustodyEventKind Kind, int Actor, long Transfer, int From, int To)[] steps)
    {
        var verdict = Verify(SignedSteps(steps));
        Assert.Equal(ChainFailure.TransitionViolation, verdict.Failure);
        return verdict;
    }

    /// <summary>Registration to custodian 4, then <paramref name="steps"/> signed as given, with no transfer rows.</summary>
    private static Signed SignedSteps(params (CustodyEventKind Kind, int Actor, long Transfer, int From, int To)[] steps)
    {
        var signer = new Signer(NewEvidence());
        signer.Append(EvidenceRegistered, 1, null, null, 4);
        foreach (var step in steps)
            signer.Append(step.Kind, step.Actor, step.Transfer, step.From, step.To);
        return new Signed(signer.Evidence, signer.Chain, []);
    }

    private static void Set(object target, string property, object? value) => target.GetType().GetProperty(property)!.SetValue(target, value);

    /// <summary>Signs events in order with k1, one hour apart from registration, and moves the head.</summary>
    private sealed class Signer(Evidence evidence)
    {
        public Evidence Evidence => evidence;

        public List<CustodyEvent> Chain { get; } = [];

        public DateTime NextAt => Registered.AddHours(Chain.Count);

        public void Append(CustodyEventKind kind, int actor, long? transfer, int? from, int to, string? notes = null)
        {
            var (seq, at, previous) = (Chain.Count + 1, NextAt, Chain.Count == 0 ? null : Chain[^1].Mac);
            var genesis = kind == EvidenceRegistered;
            var link = new ChainLink(evidence.Code, seq, kind, at, actor, from, to, notes ?? $"nota {seq}",
                genesis ? SHA256.HashData(Content) : null, genesis ? Content.Length : null, genesis ? "text/plain" : null, "k1", previous);
            var mac = ChainHasher.Mac(Keys, link);
            Chain.Add(genesis
                ? CustodyEvent.Genesis(0, at, actor, to, link.ContentSha256!, link.ContentLength!.Value, link.MediaType!, link.Notes, "k1", mac, CanonicalEvent.Version)
                : CustodyEvent.ForTransfer(0, seq, kind, at, actor, transfer!.Value, from!.Value, to, link.Notes, "k1", previous!, mac, CanonicalEvent.Version));
            evidence.AdvanceHead(seq, mac);
        }
    }

    /// <summary>The event rebuilt with one field changed and its original MAC kept, as a direct edit would.</summary>
    private static CustodyEvent Alter(CustodyEvent e, string field)
    {
        var at = field == "OccurredAt" ? e.OccurredAtUtc.AddSeconds(1) : e.OccurredAtUtc;
        var actor = field == "Actor" ? e.ActorId + 1 : e.ActorId;
        var to = field == "To" ? 9 : e.ToCustodianId!.Value;
        var notes = field == "Notes" ? e.Notes + "." : e.Notes;
        var mac = field == "Mac" ? e.Mac.Select(b => (byte)(b ^ 1)).ToArray() : e.Mac;
        var keyId = field == "KeyId" ? "k2" : e.KeyId;

        if (e.Seq == 1)
        {
            var sha = field == "ContentSha256" ? new byte[32] : e.ContentSha256!;
            var length = field == "ContentLength" ? e.ContentLength!.Value + 1 : e.ContentLength!.Value;
            var media = field == "MediaType" ? "text/csv" : e.MediaType!;
            return CustodyEvent.Genesis(e.EvidenceId, at, actor, to, sha, length, media, notes, keyId, mac, e.CanonicalVersion);
        }

        var seq = field == "Seq" ? e.Seq + 10 : e.Seq;
        var kind = field == "Kind" ? (e.Kind == TransferAccepted ? TransferRejected : TransferAccepted) : e.Kind;
        var from = field == "From" ? 9 : e.FromCustodianId!.Value;
        var prevMac = field == "PrevMac" ? new byte[32] : e.PrevMac!;
        return CustodyEvent.ForTransfer(e.EvidenceId, seq, kind, at, actor, e.TransferId!.Value, from, to, notes, keyId, prevMac, mac, e.CanonicalVersion);
    }
}
