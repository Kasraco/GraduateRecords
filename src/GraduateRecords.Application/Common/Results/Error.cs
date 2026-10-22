namespace GraduateRecords.Application.Common.Results;

/// <summary>AGENTS §4: expected failures carry a kind, a stable code and a user-facing message.</summary>
public sealed record Error(ErrorKind Kind, string Code, string Message);
