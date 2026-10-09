using EvidenceChain.Domain.People;

namespace EvidenceChain.Application.People;

/// <summary>A user as clients see them and as tokens name them.</summary>
public sealed record UserSummary(int Id, string UserName, string DisplayName, UserRole Role);

/// <summary>A user as lists name them: no user name, which is what signs in.</summary>
public sealed record PersonSummary(int Id, string DisplayName, UserRole Role);

/// <summary>Read side of the user list.</summary>
public interface IUserDirectory
{
    /// <summary>The user with this user name, or null.</summary>
    Task<UserSummary?> FindByUserNameAsync(string userName, CancellationToken cancellationToken);

    /// <summary>The user with this id, or null.</summary>
    Task<UserSummary?> FindAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Everyone, or everyone with <paramref name="role"/>, by display name (then id, for a stable order).</summary>
    Task<IReadOnlyList<PersonSummary>> ListAsync(UserRole? role, CancellationToken cancellationToken);
}
