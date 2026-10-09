using EvidenceChain.Domain.People;

namespace EvidenceChain.Domain.Custody;

/// <summary>Things a user can do to a transfer.</summary>
public enum TransferCommand
{
    Request,
    Accept,
    Reject,
}

/// <summary>Why a command is refused; the API maps each to its own status.</summary>
public enum TransitionError
{
    None,

    /// <summary>The role never issues this command (403).</summary>
    RoleNotAllowed,

    /// <summary>Only the designated recipient decides (403).</summary>
    NotRecipient,

    /// <summary>The command does not apply in the current state, e.g. accepting twice (409).</summary>
    InvalidTransition,
}

/// <summary>Outcome of <see cref="TransferTransitions.Decide"/>: the next status, or why not.</summary>
public readonly record struct TransitionDecision(TransferStatus? Next, TransitionError Error)
{
    public bool IsAllowed => Error == TransitionError.None;
}

/// <summary>
/// The transfer state machine as one pure table. Investigadores request; only the designated recipient,
/// a Custodio, accepts or rejects; decided transfers are final. Checks run role, then recipient, then state.
/// </summary>
public static class TransferTransitions
{
    /// <summary>
    /// Decides <paramref name="command"/>. For a request, <paramref name="current"/> is the evidence's pending
    /// transfer status (null when none); otherwise it is the transfer's own status.
    /// </summary>
    public static TransitionDecision Decide(TransferStatus? current, TransferCommand command, UserRole role, bool isRecipient)
    {
        var requiredRole = command == TransferCommand.Request ? UserRole.Investigador : UserRole.Custodio;
        if (role != requiredRole)
            return Refuse(TransitionError.RoleNotAllowed);
        if (command != TransferCommand.Request && !isRecipient)
            return Refuse(TransitionError.NotRecipient);

        return (command, current) switch
        {
            (TransferCommand.Request, not TransferStatus.Pending) => new(TransferStatus.Pending, TransitionError.None),
            (TransferCommand.Accept, TransferStatus.Pending) => new(TransferStatus.Accepted, TransitionError.None),
            (TransferCommand.Reject, TransferStatus.Pending) => new(TransferStatus.Rejected, TransitionError.None),
            _ => Refuse(TransitionError.InvalidTransition),
        };
    }

    private static TransitionDecision Refuse(TransitionError error) => new(null, error);
}

/// <summary>A transfer command refused by <see cref="TransferTransitions"/>.</summary>
public abstract class TransferRuleException(TransitionError error, string message) : Exception(message)
{
    public TransitionError Error { get; } = error;

    /// <summary>Throws the exception matching a refused decision.</summary>
    internal static void ThrowIfRefused(TransitionDecision decision, TransferCommand command, UserRole role, CustodyTransfer? transfer)
    {
        switch (decision.Error)
        {
            case TransitionError.None:
                return;
            case TransitionError.RoleNotAllowed:
                throw new RoleNotAllowedException(role, command);
            case TransitionError.NotRecipient:
                throw new NotRecipientException(command);
            default:
                throw new InvalidTransitionException(command, transfer);
        }
    }
}

/// <summary>The role never issues the command.</summary>
public sealed class RoleNotAllowedException(UserRole role, TransferCommand command)
    : TransferRuleException(TransitionError.RoleNotAllowed, $"A {role} cannot {command.ToString().ToLowerInvariant()} a transfer.")
{
    public UserRole Role { get; } = role;
}

/// <summary>Someone other than the recipient tried to decide.</summary>
public sealed class NotRecipientException(TransferCommand command)
    : TransferRuleException(TransitionError.NotRecipient, $"Only the recipient can {command.ToString().ToLowerInvariant()} this transfer.");

/// <summary>
/// The command does not fit the current state. Carries who acted and when, so the API can answer 409
/// with what actually happened (the second tab of the two-tab demo).
/// </summary>
public sealed class InvalidTransitionException(TransferCommand command, CustodyTransfer? transfer)
    : TransferRuleException(TransitionError.InvalidTransition,
        transfer is null ? $"Cannot {command.ToString().ToLowerInvariant()}: the evidence already has a pending transfer."
        : $"Cannot {command.ToString().ToLowerInvariant()} transfer {transfer.TransferId}: it is {transfer.Status}.")
{
    /// <summary>The transfer acted on, or the one already pending when a request is refused.</summary>
    public CustodyTransfer? Transfer { get; } = transfer;

    public TransferStatus? CurrentStatus { get; } = transfer?.Status;

    public int? ActedById { get; } = transfer?.DecidedById;

    public DateTime? ActedAtUtc { get; } = transfer?.DecidedAtUtc;
}
