using System.Security.Cryptography;
using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.Custody;

namespace EvidenceChain.Domain.Integrity;

/// <summary>Why a chain fails verification; <see cref="ChainFailureCodes.Code"/> gives the API's code.</summary>
public enum ChainFailure
{
    SequenceGap,
    PrevLinkMismatch,
    UnknownKey,
    MacMismatch,
    HeadMismatch,
    ContentHashMismatch,
    CustodyProjectionMismatch,

    /// <summary>The signed events themselves describe a transfer the state machine forbids.</summary>
    TransitionViolation,
}

public static class ChainFailureCodes
{
    /// <summary>The wire code, e.g. MAC_MISMATCH.</summary>
    public static string Code(this ChainFailure failure) => failure switch
    {
        ChainFailure.SequenceGap => "SEQUENCE_GAP",
        ChainFailure.PrevLinkMismatch => "PREV_LINK_MISMATCH",
        ChainFailure.UnknownKey => "UNKNOWN_KEY",
        ChainFailure.MacMismatch => "MAC_MISMATCH",
        ChainFailure.HeadMismatch => "HEAD_MISMATCH",
        ChainFailure.ContentHashMismatch => "CONTENT_HASH_MISMATCH",
        ChainFailure.CustodyProjectionMismatch => "CUSTODY_PROJECTION_MISMATCH",
        _ => "TRANSITION_VIOLATION",
    };
}

/// <summary>Result of verifying one evidence: valid, or the first failure and the position it was found at.</summary>
public sealed record ChainVerdict(bool IsValid, int VerifiedThroughSeq, ChainFailure? Failure, int? FailedAtSeq, string? Detail)
{
    public static ChainVerdict Valid(int through) => new(true, through, null, null, null);

    public static ChainVerdict Fail(ChainFailure failure, int atSeq, string detail) => new(false, atSeq - 1, failure, atSeq, detail);
}

/// <summary>
/// Verifies an evidence end to end. The signed chain comes first, event by event: sequence, previous MAC, key and MAC,
/// then the content against the genesis commitment and each transfer step against the state machine; then the head.
/// Only a sound chain is compared with the rows that can be updated directly (registration, custodians, content
/// metadata, transfers), and the earliest disagreement is reported.
/// </summary>
public static class ChainVerifier
{
    /// <summary>
    /// Returns the first failure, or valid. <paramref name="chain"/> must be in sequence order and
    /// <paramref name="transfers"/> must be every transfer row of <paramref name="evidence"/>.
    /// </summary>
    public static ChainVerdict Verify(
        IntegrityKeyRing keys, Evidence evidence, IReadOnlyList<CustodyEvent> chain, IReadOnlyCollection<CustodyTransfer> transfers, EvidenceContent content)
    {
        if (chain.Count == 0)
            return ChainVerdict.Fail(ChainFailure.SequenceGap, 1, "La evidencia no tiene eventos de custodia.");

        var history = new History();
        byte[]? previous = null;
        for (var i = 0; i < chain.Count; i++)
        {
            var e = chain[i];
            var expected = i + 1;
            if (e.Seq != expected)
                return ChainVerdict.Fail(ChainFailure.SequenceGap, expected, $"Se esperaba el evento {expected} y aparece el {e.Seq}.");
            if (!SameBytes(e.PrevMac, previous))
                return ChainVerdict.Fail(ChainFailure.PrevLinkMismatch, expected, "El evento no enlaza con el MAC del anterior.");
            if (!keys.TryGetKey(e.KeyId, out _))
                return ChainVerdict.Fail(ChainFailure.UnknownKey, expected, $"Clave de integridad desconocida: {e.KeyId}.");
            if (!MacMatches(keys, evidence.Code, e))
                return ChainVerdict.Fail(ChainFailure.MacMismatch, expected, "El MAC no corresponde a los datos del evento.");
            if (history.Apply(e) is { } violation)
                return ChainVerdict.Fail(ChainFailure.TransitionViolation, expected, violation);
            if (i == 0 && ContentMismatch(e, content) is { } mismatch)
                return ChainVerdict.Fail(ChainFailure.ContentHashMismatch, 1, mismatch);
            previous = e.Mac;
        }

        if (evidence.EventCount != chain.Count || !SameBytes(evidence.HeadMac, previous))
            return ChainVerdict.Fail(ChainFailure.HeadMismatch, chain.Count, "La cabecera de la evidencia no coincide con su último evento.");

        return ProjectionMismatches(evidence, chain[0], history, transfers, chain.Count).MinBy(f => f.FailedAtSeq) ?? ChainVerdict.Valid(chain.Count);
    }

    /// <summary>The genesis commitment against the bytes and the stored hash, length and media type.</summary>
    private static string? ContentMismatch(CustodyEvent genesis, EvidenceContent content)
    {
        var bytes = content.Bytes;
        if (bytes.Length != genesis.ContentLength || !SameBytes(SHA256.HashData(bytes), genesis.ContentSha256))
            return "El contenido no coincide con el hash comprometido al registrarla.";
        if (content.ByteLength != genesis.ContentLength || !SameBytes(content.Sha256, genesis.ContentSha256) || content.MediaType != genesis.MediaType)
            return "Los metadatos guardados del contenido no coinciden con el registro.";
        return null;
    }

    /// <summary>
    /// The custody the signed events describe, replayed through the transfer rules: one open request at a time,
    /// from the current custodian to someone else, closed once by its recipient. Transfer ids sit outside the MAC,
    /// so this replay and the row checks are what tie each event to its transfer.
    /// </summary>
    private sealed class History
    {
        private int? _open;
        private readonly HashSet<long> _seen = [];

        public int Custodian { get; private set; }

        public List<(CustodyEvent Request, CustodyEvent? Decision)> Transfers { get; } = [];

        /// <summary>Applies an authenticated event; returns why it breaks the rules, or null.</summary>
        public string? Apply(CustodyEvent e)
        {
            if (e.Seq == 1)
            {
                if (e.Kind != CustodyEventKind.EvidenceRegistered || e.ToCustodianId is not { } initial)
                    return "El primer evento no es el registro de la evidencia.";
                Custodian = initial;
                return null;
            }

            switch (e.Kind)
            {
                case CustodyEventKind.TransferRequested:
                    if (_open is not null)
                        return "Se solicita una transferencia con otra todavía pendiente.";
                    if (e.TransferId is not { } id || !_seen.Add(id))
                        return "La solicitud no abre una transferencia nueva.";
                    if (e.FromCustodianId != Custodian || e.ToCustodianId is null || e.ToCustodianId == Custodian || e.ToCustodianId == e.ActorId)
                        return "La solicitud no parte del custodio actual hacia otro custodio.";
                    _open = Transfers.Count;
                    Transfers.Add((e, null));
                    return null;

                case CustodyEventKind.TransferAccepted or CustodyEventKind.TransferRejected:
                    if (_open is not { } index)
                        return "Se decide una transferencia que no está pendiente.";
                    var request = Transfers[index].Request;
                    if (e.TransferId != request.TransferId || e.FromCustodianId != request.FromCustodianId
                        || e.ToCustodianId != request.ToCustodianId || e.ActorId != request.ToCustodianId)
                        return "La decisión no la toma el destinatario de la transferencia pendiente.";
                    Transfers[index] = (request, e);
                    if (e.Kind == CustodyEventKind.TransferAccepted)
                        Custodian = request.ToCustodianId!.Value;
                    _open = null;
                    return null;

                default:
                    return "Solo el primer evento puede registrar la evidencia.";
            }
        }
    }

    /// <summary>
    /// Every disagreement between the directly updatable columns and the replayed events. A transfer's reason and
    /// decision notes are the notes of its request and decision events.
    /// </summary>
    private static IEnumerable<ChainVerdict> ProjectionMismatches(
        Evidence evidence, CustodyEvent genesis, History history, IReadOnlyCollection<CustodyTransfer> rows, int last)
    {
        static ChainVerdict Mismatch(int seq, string detail) => ChainVerdict.Fail(ChainFailure.CustodyProjectionMismatch, seq, detail);

        if (evidence.InitialCustodianId != genesis.ToCustodianId || evidence.RegisteredById != genesis.ActorId || evidence.RegisteredAtUtc != genesis.OccurredAtUtc)
            yield return Mismatch(1, "El registro de la evidencia no coincide con su evento de registro.");
        if (evidence.CurrentCustodianId != history.Custodian)
            yield return Mismatch(last, "El custodio actual no coincide con las transferencias aceptadas.");
        if (rows.Any(r => r.EvidenceId != evidence.EvidenceId))
            yield return Mismatch(last, "Hay transferencias de otra evidencia.");

        var byId = rows.Where(r => r.EvidenceId == evidence.EvidenceId).ToLookup(r => r.TransferId);
        foreach (var (request, decision) in history.Transfers)
        {
            var matching = byId[request.TransferId!.Value].ToList();
            if (matching.Count != 1)
            {
                yield return Mismatch(request.Seq, matching.Count == 0 ? "Falta la transferencia que registra el evento." : "La transferencia está duplicada.");
                continue;
            }

            var row = matching[0];
            if (row.FromCustodianId != request.FromCustodianId || row.ToCustodianId != request.ToCustodianId
                || row.RequestedById != request.ActorId || row.RequestedAtUtc != request.OccurredAtUtc || row.Reason != request.Notes)
                yield return Mismatch(request.Seq, "La solicitud guardada no coincide con su evento.");

            var status = decision?.Kind switch
            {
                null => TransferStatus.Pending,
                CustodyEventKind.TransferAccepted => TransferStatus.Accepted,
                _ => TransferStatus.Rejected,
            };
            if (row.Status != status || row.DecidedAtUtc != decision?.OccurredAtUtc || row.DecidedById != decision?.ActorId
                || (decision is not null && (row.DecisionNotes ?? string.Empty) != decision.Notes))
                yield return Mismatch(decision?.Seq ?? last, "El estado guardado de la transferencia no coincide con sus eventos.");
        }

        var recorded = history.Transfers.Select(t => t.Request.TransferId!.Value).ToHashSet();
        if (byId.Any(g => !recorded.Contains(g.Key)))
            yield return Mismatch(last, "Hay transferencias sin eventos de custodia.");
    }

    private static bool MacMatches(IntegrityKeyRing keys, string code, CustodyEvent e)
    {
        try
        {
            return ChainHasher.Matches(ChainHasher.Mac(keys, ChainLink.From(code, e)), e.Mac);
        }
        catch (NotSupportedException)
        {
            return false; // an edited canonical version cannot be re-encoded, so it cannot match
        }
    }

    private static bool SameBytes(byte[]? a, byte[]? b) =>
        a is null || b is null ? a is null && b is null : CryptographicOperations.FixedTimeEquals(a, b);
}
