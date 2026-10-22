namespace GraduateRecords.Application.Common.Results;

/// <summary>AGENTS §4: the kinds of expected failure a handler may return.</summary>
public enum ErrorKind
{
    Validation,
    NotFound,
    Forbidden,
    Conflict,
    Unavailable,
}
