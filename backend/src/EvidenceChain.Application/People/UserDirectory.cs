using EvidenceChain.Domain.People;

namespace EvidenceChain.Application.People;

/// <summary>A user as clients see them and as tokens name them.</summary>
public sealed record UserSummary(int Id, string UserName, string DisplayName, UserRole Role);

/// <summary>Read side of the user list.</summary>
public interface IUserDirectory
{
    /// <summary>The user with this user name, or null.</summary>
    Task<UserSummary?> FindByUserNameAsync(string userName, CancellationToken cancellationToken);

    /// <summary>The user with this id, or null.</summary>
    Task<UserSummary?> FindAsync(int userId, CancellationToken cancellationToken);
}
