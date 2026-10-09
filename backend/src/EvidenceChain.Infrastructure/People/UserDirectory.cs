using EvidenceChain.Application.People;
using EvidenceChain.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvidenceChain.Infrastructure.People;

internal sealed class UserDirectory(AppDbContext db) : IUserDirectory
{
    public Task<UserSummary?> FindByUserNameAsync(string userName, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().Where(u => u.UserName == userName)
            .Select(u => new UserSummary(u.UserId, u.UserName, u.DisplayName, u.Role)).SingleOrDefaultAsync(cancellationToken);

    public Task<UserSummary?> FindAsync(int userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().Where(u => u.UserId == userId)
            .Select(u => new UserSummary(u.UserId, u.UserName, u.DisplayName, u.Role)).SingleOrDefaultAsync(cancellationToken);
}
