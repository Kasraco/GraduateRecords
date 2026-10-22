using GraduateRecords.Domain.Auditing;

namespace GraduateRecords.Application.Common.Auditing;

/// <summary>
/// One audit record (AGENTS §14). <see cref="ActorUserId"/> is the stable identity
/// reference and <see cref="ActorDisplayName"/> is the display-name snapshot at
/// event time. Values that are sensitive (National ID, DataJson, tokens, OTP codes,
/// passwords) are never placed in any field, including <see cref="DetailsJson"/>.
/// </summary>
public sealed record AuditEntry(
    DateTimeOffset OccurredAtUtc,
    string? ActorUserId,
    string? ActorDisplayName,
    string Action,
    string EntityType,
    string EntityId,
    AuditResult Result,
    string Summary,
    string? DetailsJson,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId);
