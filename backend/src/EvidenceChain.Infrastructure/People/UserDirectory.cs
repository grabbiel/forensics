using EvidenceChain.Application.People;
using EvidenceChain.Domain.People;
using EvidenceChain.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvidenceChain.Infrastructure.People;

internal sealed class UserDirectory(AppDbContext db) : IUserDirectory
{
    public Task<UserSummary?> FindByUserNameAsync(string userName, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().Where(u => u.UserName == userName)
            .Select(u => new UserSummary(u.UserId, u.UserName, u.DisplayName, u.Role)).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PersonSummary>> ListAsync(UserRole? role, CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().Where(u => role == null || u.Role == role).OrderBy(u => u.DisplayName).ThenBy(u => u.UserId)
            .Select(u => new PersonSummary(u.UserId, u.DisplayName, u.Role)).ToListAsync(cancellationToken);

    public Task<UserSummary?> FindAsync(int userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().Where(u => u.UserId == userId)
            .Select(u => new UserSummary(u.UserId, u.UserName, u.DisplayName, u.Role)).SingleOrDefaultAsync(cancellationToken);
}
