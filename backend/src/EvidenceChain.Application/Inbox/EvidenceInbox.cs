namespace EvidenceChain.Application.Inbox;

/// <summary>One inbox row as returned to clients.</summary>
public sealed record EvidenceSummary(string Code, string TypeCode, DateOnly RegisteredOn, string Description);

/// <summary>Inbox filters. Tracer version; custodian, status and keyset cursor arrive on Day 3.</summary>
public sealed record EvidenceInboxFilter(string? Query, string? TypeCode, int Limit);

/// <summary>Read side of the inbox.</summary>
public interface IEvidenceInboxQuery
{
    /// <summary>Returns the newest evidence matching the filter.</summary>
    Task<IReadOnlyList<EvidenceSummary>> ListAsync(EvidenceInboxFilter filter, CancellationToken cancellationToken);
}
