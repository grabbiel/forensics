using EvidenceChain.Domain.Catalog;
using EvidenceChain.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvidenceChain.Seeder;

/// <summary>Five hand-picked rows for the Day 1 tracer bullet; replaced by the synthetic dataset on Day 2.</summary>
internal static class TracerData
{
    /// <summary>Inserts the tracer rows when the table is empty and returns their codes.</summary>
    public static async Task<IReadOnlyList<string>> InsertIfEmptyAsync(AppDbContext db, DateOnly todayUtc, CancellationToken cancellationToken)
    {
        if (await db.Evidence.AnyAsync(cancellationToken))
            return [];

        Evidence[] rows =
        [
            new(EvidenceTypes.Log, todayUtc, 1, "Log del firewall fw-edge-01, 14:00-16:00 UTC"),
            new(EvidenceTypes.Log, todayUtc, 2, "Log de la pasarela VPN vpn-02, sesión nocturna"),
            new(EvidenceTypes.Csv, todayUtc, 1, "Extracto bancario, cuenta XA00-0041, septiembre 2026"),
            new(EvidenceTypes.Csv, todayUtc, 2, "Transacciones con tarjeta, lote 7 del comercio"),
            new(EvidenceTypes.Eml, todayUtc, 1, "Correo de j.perez@example.test: «Transferencia urgente»"),
        ];

        db.Evidence.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        return rows.Select(r => r.Code).ToArray();
    }
}
