using System.ComponentModel.DataAnnotations;
using EvidenceChain.Application.Custody;
using EvidenceChain.Application.Idempotency;
using EvidenceChain.Domain.Catalog;
using EvidenceChain.Domain.Custody;
using EvidenceChain.Domain.People;
using EvidenceChain.Infrastructure.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EvidenceChain.IntegrationTests.Probes;

/// <summary>Test-only endpoints that reach error paths no real endpoint reaches yet. Mounted by <see cref="ApiFactory.Probes"/>.</summary>
[ApiController]
[Route("api/v1/probe")]
[AllowAnonymous]
public sealed class ProbeController : ControllerBase
{
    /// <summary>The version every probe transfer is at: 0x7D1.</summary>
    public const string TransferETag = "\"00000000000007d1\"";

    [HttpGet("validate")]
    public IActionResult Validate([FromQuery, Range(1, 5)] int n) => Ok(n);

    [HttpGet("throw/{kind}")]
    public IActionResult Throw(string kind) => throw kind switch
    {
        "pending-unknown" => new InvalidTransitionException(TransferCommand.Request, null),
        "pending-exists" => new InvalidTransitionException(TransferCommand.Request, PendingTransfer()),
        "already-accepted" => new InvalidTransitionException(TransferCommand.Accept, AcceptedTransfer()),
        "stale" => new StaleVersionException(AcceptedTransfer()),
        "not-recipient" => new NotRecipientException(TransferCommand.Accept),
        "role" => new RoleNotAllowedException(UserRole.Supervisor, TransferCommand.Request),
        "key-reused" => new IdempotencyKeyReusedException(Guid.Empty),
        "in-flight" => new IdempotencyInFlightException(Guid.Empty),
        "exhausted" => new DailyIndexExhaustedException(EvidenceTypes.Log, new DateOnly(2026, 10, 9)),
        "busy" => new ConcurrentWriteException("LOG202610090001"),
        "invalid-field" => new InvalidRequestException("toCustodianId", "Send it to a user with the Custodio role."),
        _ => new InvalidOperationException("Unexpected failure with a secret detail."),
    };

    /// <summary>Transfer 7: requested by investigador.demo (1) at 09:00 UTC from custodian 5 to custodio.demo (4), who accepted it at 10:00 UTC.</summary>
    public static CustodyTransfer AcceptedTransfer() => ProbeTransfer(accept: true);

    /// <summary>Transfer 7 before custodio.demo decided it.</summary>
    public static CustodyTransfer PendingTransfer() => ProbeTransfer(accept: false);

    private static CustodyTransfer ProbeTransfer(bool accept)
    {
        var at = new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);
        var evidence = new Evidence(EvidenceTypes.Log, DateOnly.FromDateTime(at), 1, "Probe", at, at,
            registeredById: 1, initialCustodianId: 5, new EvidenceContent([1], "text/plain"));
        var transfer = CustodyTransfer.Request(evidence, new Actor(1, UserRole.Investigador), new Actor(4, UserRole.Custodio),
            pendingTransfer: null, at.AddHours(1), "Probe", Guid.CreateVersion7(), new byte[32]);
        if (accept)
            transfer.Accept(evidence, new Actor(4, UserRole.Custodio), at.AddHours(2), null, Guid.CreateVersion7(), new byte[32]);

        // What the database would have assigned.
        typeof(CustodyTransfer).GetProperty(nameof(CustodyTransfer.TransferId))!.SetValue(transfer, 7L);
        typeof(CustodyTransfer).GetProperty(nameof(CustodyTransfer.RowVersion))!.SetValue(transfer, new byte[] { 0, 0, 0, 0, 0, 0, 0x07, 0xd1 });
        return transfer;
    }
}
