namespace GraduateRecords.Application.Common.Auditing;

/// <summary>
/// AGENTS §14 audit port. Authentication events (Phase 1A) are ordinary
/// operational events: implementations must be best-effort and must never throw
/// for them. The classified sensitive reads SR1–SR5 (Phase 1C and later) are
/// fail-closed at the call site instead.
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
