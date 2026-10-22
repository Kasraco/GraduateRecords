namespace GraduateRecords.Domain.Auditing;

/// <summary>AGENTS §14: stored as a string on the audit record.</summary>
public enum AuditResult
{
    Success,
    Denied,
    Failed,
}
