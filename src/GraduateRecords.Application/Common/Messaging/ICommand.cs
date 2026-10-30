namespace GraduateRecords.Application.Common.Messaging;

/// <summary>A state-changing use case input (AGENTS §4, CQRS-lite).</summary>
public interface ICommand<TResult>;
