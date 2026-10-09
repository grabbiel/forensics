using System.Text;
using EvidenceChain.Domain.Catalog;
using EvidenceChain.Infrastructure.Persistence;
using EvidenceChain.SyntheticData;
using Microsoft.EntityFrameworkCore;

namespace EvidenceChain.Seeder;

/// <summary>Five hand-picked rows for the tracer bullet, without custody events; `seed` replaces them.</summary>
internal static class TracerData
{
    /// <summary>Inserts the users and tracer rows when no evidence exists, and returns the codes.</summary>
    public static async Task<IReadOnlyList<string>> InsertIfEmptyAsync(AppDbContext db, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (await db.Evidence.AnyAsync(cancellationToken))
            return [];

        await DemoUsers.EnsureAsync(db, cancellationToken);

        var today = DateOnly.FromDateTime(nowUtc);
        var investigator = DemoUsers.Id("investigador.demo");
        (string Type, short No, string Description, string Custodian)[] rows =
        [
            (EvidenceTypes.Log, 1, "Log del firewall fw-edge-01, 14:00-16:00 UTC", SyntheticPeople.DemoCustodian),
            (EvidenceTypes.Log, 2, "Log de la pasarela VPN vpn-02, sesión nocturna", "nuria.paredes"),
            (EvidenceTypes.Csv, 1, "Extracto bancario, cuenta XA00-0041, septiembre 2026", "oscar.villalba"),
            (EvidenceTypes.Csv, 2, "Transacciones con tarjeta, lote 7 del comercio", "carmen.robles"),
            (EvidenceTypes.Eml, 1, "Correo de j.perez@example.test: «Transferencia urgente»", SyntheticPeople.DemoCustodian),
        ];

        var evidences = rows.Select(r => new Evidence(
            r.Type, today, r.No, r.Description, capturedAtUtc: nowUtc.AddHours(-2), registeredAtUtc: nowUtc,
            investigator, DemoUsers.Id(r.Custodian),
            new EvidenceContent(Encoding.UTF8.GetBytes(r.Description + "\n"), DatasetBuilder.MediaType(r.Type)))).ToArray();

        db.Evidence.AddRange(evidences);
        await db.SaveChangesAsync(cancellationToken);
        return evidences.Select(e => e.Code).ToArray();
    }
}
