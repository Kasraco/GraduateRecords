using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Application.Common.Time;
using GraduateRecords.Domain.Auditing;

namespace GraduateRecords.Application.Features.Administration;

/// <summary>
/// One place that shapes the Phase 1A best-effort security events of the
/// administration use cases (AGENTS §14: every state change and every
/// authorization denial is declared and written with a stable actor snapshot;
/// ADR-0016 §12.4 / ADR-0017 §22: in Phase 1A these go to the structured Serilog
/// sink only — the durable append-only <c>AuditLog</c> arrives with Phase 1C, so
/// no transactional or guaranteed recording is claimed here).
/// <para>Payloads are ids, names, role names and outcome only — never a
/// National ID, mobile number, credential or token.</para>
/// </summary>
internal static class AdministrationAudit
{
    public static Task SuccessAsync(
        IAuditWriter auditWriter,
        IClock clock,
        ICurrentUser actor,
        string action,
        string entityId,
        string summary,
        string? detailsJson,
        RequestAuditContext request,
        CancellationToken cancellationToken) =>
        WriteAsync(
            auditWriter,
            clock,
            actor,
            action,
            entityId,
            AuditResult.Success,
            summary,
            detailsJson,
            request,
            cancellationToken);

    public static Task DeniedAsync(
        IAuditWriter auditWriter,
        IClock clock,
        ICurrentUser actor,
        string action,
        string entityId,
        string summary,
        RequestAuditContext request,
        CancellationToken cancellationToken) =>
        WriteAsync(
            auditWriter,
            clock,
            actor,
            action,
            entityId,
            AuditResult.Denied,
            summary,
            detailsJson: null,
            request,
            cancellationToken);

    private static Task WriteAsync(
        IAuditWriter auditWriter,
        IClock clock,
        ICurrentUser actor,
        string action,
        string entityId,
        AuditResult result,
        string summary,
        string? detailsJson,
        RequestAuditContext request,
        CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(
            new AuditEntry(
                clock.UtcNow,
                actor.UserId,
                actor.DisplayName,
                action,
                EntityType: "User",
                entityId,
                result,
                summary,
                detailsJson,
                request.ClientIpAddress,
                request.UserAgent,
                request.CorrelationId),
            cancellationToken);
}
