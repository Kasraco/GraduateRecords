namespace GraduateRecords.Application.Common.Auditing;

/// <summary>
/// Request context an action passes into its command so the handler can write a
/// useful audit entry without touching <c>HttpContext</c> (Application must not
/// depend on ASP.NET Core, AGENTS §2). All fields are optional, non-sensitive
/// observability data — never a token, a password or a full National ID
/// (AGENTS §14).
/// </summary>
public sealed record RequestAuditContext(
    string? ClientIpAddress = null,
    string? UserAgent = null,
    string? CorrelationId = null);
