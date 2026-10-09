using EvidenceChain.Application.Inbox;
using EvidenceChain.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvidenceChain.Infrastructure.Inbox;

/// <summary>Tracer inbox query with EF Core; Day 3 swaps in Dapper over the A4 projection with keyset paging.</summary>
internal sealed class EvidenceInboxQuery(AppDbContext db) : IEvidenceInboxQuery
{
    /// <summary>Newest first; text matches a code prefix or the description.</summary>
    public async Task<IReadOnlyList<EvidenceSummary>> ListAsync(EvidenceInboxFilter filter, CancellationToken cancellationToken)
    {
        var query = db.Evidence.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.TypeCode))
            query = query.Where(e => e.TypeCode == filter.TypeCode);

        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var text = filter.Query.Trim();
            var codePrefix = text.ToUpperInvariant(); // codes are upper-case under a binary collation
            query = query.Where(e => e.Code.StartsWith(codePrefix) || e.Description.Contains(text));
        }

        return await query
            .OrderByDescending(e => e.CodeDateUtc)
            .ThenByDescending(e => e.EvidenceId)
            .Take(filter.Limit)
            .Select(e => new EvidenceSummary(e.Code, e.TypeCode, e.CodeDateUtc, e.Description))
            .ToListAsync(cancellationToken);
    }
}
