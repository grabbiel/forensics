using EvidenceChain.Domain.Custody;
using EvidenceChain.Domain.People;

namespace EvidenceChain.UnitTests.Custody;

public sealed class TransferTransitionsTests
{
    private static readonly TransferStatus?[] States = [null, TransferStatus.Pending, TransferStatus.Accepted, TransferStatus.Rejected];

    /// <summary>Every state × command × role × recipient flag: 4 × 3 × 3 × 2 = 72 cases.</summary>
    public static TheoryData<TransferStatus?, TransferCommand, UserRole, bool> AllCases()
    {
        var data = new TheoryData<TransferStatus?, TransferCommand, UserRole, bool>();
        foreach (var state in States)
        foreach (var command in Enum.GetValues<TransferCommand>())
        foreach (var role in Enum.GetValues<UserRole>())
        foreach (var isRecipient in new[] { false, true })
            data.Add(state, command, role, isRecipient);
        return data;
    }

    [Fact]
    public void The_table_has_seventy_two_cases()
    {
        Assert.Equal(72, AllCases().Count);
    }

    [Theory, MemberData(nameof(AllCases))]
    public void Every_combination_follows_the_rules(TransferStatus? state, TransferCommand command, UserRole role, bool isRecipient)
    {
        Assert.Equal(Expected(state, command, role, isRecipient), TransferTransitions.Decide(state, command, role, isRecipient));
    }

    /// <summary>The rules written out independently of the implementation.</summary>
    private static TransitionDecision Expected(TransferStatus? state, TransferCommand command, UserRole role, bool isRecipient)
    {
        var refused = (TransitionError error) => new TransitionDecision(null, error);

        if (command == TransferCommand.Request)
        {
            if (role != UserRole.Investigador)
                return refused(TransitionError.RoleNotAllowed); // only investigators request
            return state == TransferStatus.Pending
                ? refused(TransitionError.InvalidTransition) // one open request per evidence
                : new TransitionDecision(TransferStatus.Pending, TransitionError.None);
        }

        if (role != UserRole.Custodio)
            return refused(TransitionError.RoleNotAllowed); // only custodians decide
        if (!isRecipient)
            return refused(TransitionError.NotRecipient); // and only the one it was sent to
        if (state != TransferStatus.Pending)
            return refused(TransitionError.InvalidTransition); // decisions are final

        return new TransitionDecision(command == TransferCommand.Accept ? TransferStatus.Accepted : TransferStatus.Rejected, TransitionError.None);
    }
}
