using GraduateRecords.Application.Common.Results;

namespace GraduateRecords.Application.Common.Messaging;

/// <summary>
/// AGENTS §4: a handler is a plain class with a single HandleAsync method.
/// Handlers are registered by assembly scan in the Web composition root.
/// </summary>
public interface IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}
