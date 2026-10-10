using EvidenceChain.Application.Common;
using EvidenceChain.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EvidenceChain.Infrastructure.People;

/// <summary>Every user by id, for naming people in views; there are a handful.</summary>
internal sealed class PeopleIndex(Dictionary<int, PersonRef> byId)
{
    public static async Task<PeopleIndex> LoadAsync(AppDbContext db, CancellationToken cancellationToken) =>
        new(await db.Users.AsNoTracking().ToDictionaryAsync(u => u.UserId, u => new PersonRef(u.UserId, u.DisplayName), cancellationToken));

    public PersonRef Of(int id) => byId.TryGetValue(id, out var person) ? person : new PersonRef(id, $"#{id}");
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
