using EvidenceChain.Domain.People;
using EvidenceChain.Infrastructure.Persistence;
using EvidenceChain.SyntheticData;
using Microsoft.EntityFrameworkCore;

namespace EvidenceChain.Seeder;

/// <summary>The synthetic dataset's 11 people as application users, with the same fixed ids.</summary>
internal static class DemoUsers
{
    /// <summary>Inserts the users when the table is empty.</summary>
    public static async Task EnsureAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken))
            return;

        db.Users.AddRange(SyntheticPeople.All.Select(p =>
            new User(p.Id, p.UserName, p.DisplayName, p.Email, Enum.Parse<UserRole>(p.Role.ToString()))));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Id of a seeded user by user name.</summary>
    public static int Id(string userName) => SyntheticPeople.All.Single(p => p.UserName == userName).Id;
}
