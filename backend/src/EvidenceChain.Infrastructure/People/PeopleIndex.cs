using System.Collections.Frozen;
using EvidenceChain.Application.Common;
using EvidenceChain.Domain.People;
using EvidenceChain.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EvidenceChain.Infrastructure.People;

/// <summary>
/// Every user by id, for naming people in views, and who the Supervisors are, for notifications; there are a handful,
/// and they only change with the seed. Both come from one read of the table, so a cache miss costs one query.
/// </summary>
internal sealed class PeopleIndex(Dictionary<int, PersonRef> byId, FrozenSet<int> supervisors)
{
    public static async Task<PeopleIndex> LoadAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var users = await db.Users.AsNoTracking().Select(u => new { u.UserId, u.DisplayName, u.Role }).ToListAsync(cancellationToken);
        return new(users.ToDictionary(u => u.UserId, u => new PersonRef(u.UserId, u.DisplayName)),
            users.Where(u => u.Role == UserRole.Supervisor).Select(u => u.UserId).ToFrozenSet());
    }

    public PersonRef Of(int id) => byId.TryGetValue(id, out var person) ? person : new PersonRef(id, $"#{id}");

    /// <summary>
    /// The Supervisors as of this load. One added later is told only about transfer steps taken after a load that
    /// includes them, at most <see cref="PeopleIndexProvider.Lifetime"/> later; earlier steps are not backfilled.
    /// </summary>
    public IReadOnlySet<int> Supervisors { get; } = supervisors;
}

internal sealed class PeopleIndexProvider(IMemoryCache cache, AppDbContext db)
{
    private static readonly object Key = new();
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    public async Task<PeopleIndex> GetAsync(CancellationToken cancellationToken) =>
        (await cache.GetOrCreateAsync(Key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Lifetime;
            return PeopleIndex.LoadAsync(db, cancellationToken);
        }))!;
}
