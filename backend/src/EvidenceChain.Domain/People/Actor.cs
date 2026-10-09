namespace EvidenceChain.Domain.People;

/// <summary>Who is acting: the user id and the role it acts in.</summary>
public readonly record struct Actor(int UserId, UserRole Role);
