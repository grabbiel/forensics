namespace EvidenceChain.Domain.People;

/// <summary>Application roles.</summary>
public enum UserRole
{
    Investigador,
    Custodio,
    Supervisor,
}

/// <summary>A person who can act on evidence; ids are fixed by the seed, not generated.</summary>
public sealed class User
{
    /// <summary>Creates a user; the e-mail is stored as given, lower-cased.</summary>
    public User(int userId, string userName, string displayName, string email, UserRole role)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(userId, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role.");

        UserId = userId;
        UserName = userName;
        DisplayName = displayName;
        Email = email.ToLowerInvariant();
        Role = role;
    }

    // Required by EF Core for materialization.
    private User() { }

    public int UserId { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public UserRole Role { get; private set; }
}
