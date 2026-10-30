using GraduateRecords.Application.Common.Results;

namespace GraduateRecords.Application.Common.Messaging;

/// <summary>
/// AGENTS §4: a command handler is a plain class with a single HandleAsync
/// method; commands change state (queries never do). Registration by assembly
/// scan in the Web composition root is added together with the first command
/// use case (role assignment) — this checkpoint adds only the documented
/// contract, with no mediator or external messaging package.
/// </summary>
public interface ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}
