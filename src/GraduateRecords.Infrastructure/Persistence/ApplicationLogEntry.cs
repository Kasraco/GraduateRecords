namespace GraduateRecords.Infrastructure.Persistence;

/// <summary>
/// Structured application log row written by <c>DatabaseLogSink</c> into the
/// dedicated <c>log</c> schema (ADR-0006 §6.1). Never holds exception messages,
/// stack traces, secrets or personal payloads.
/// </summary>
public sealed class ApplicationLogEntry
{
    public long Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ExceptionType { get; set; }
    public string? CorrelationId { get; set; }
    public string Application { get; set; } = string.Empty;
}
