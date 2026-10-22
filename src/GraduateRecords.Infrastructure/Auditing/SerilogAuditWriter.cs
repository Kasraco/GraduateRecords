using GraduateRecords.Application.Common.Auditing;
using Microsoft.Extensions.Logging;

namespace GraduateRecords.Infrastructure.Auditing;

/// <summary>
/// Phase 1A audit sink: structured, best-effort (never throws, AGENTS §14) and
/// PII-free. The append-only <c>AuditLog</c> table, the SR1–SR5 fail-closed
/// behaviour and the audit viewer arrive with Phase 1C; this implementation keeps
/// the authentication events of ADR-0001 observable until then.
/// </summary>
public sealed class SerilogAuditWriter(ILogger<SerilogAuditWriter> logger) : IAuditWriter
{
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            logger.LogInformation(
                "Audit {AuditAction} {AuditResult} Actor={ActorUserId} ActorName={ActorDisplayName} " +
                "Entity={EntityType}/{EntityId} Summary={AuditSummary} Details={AuditDetails} " +
                "Ip={AuditIpAddress} UserAgent={AuditUserAgent} CorrelationId={CorrelationId}",
                entry.Action,
                entry.Result.ToString(),
                entry.ActorUserId,
                entry.ActorDisplayName,
                entry.EntityType,
                entry.EntityId,
                entry.Summary,
                entry.DetailsJson,
                entry.IpAddress,
                entry.UserAgent,
                entry.CorrelationId);
        }
        catch (Exception ex)
        {
            // Best-effort operational event: an audit outage must not take the request down.
            logger.LogError(ex, "Audit write failed for action {AuditAction}.", entry.Action);
        }

        return Task.CompletedTask;
    }
}
