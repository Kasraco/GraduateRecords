namespace GraduateRecords.Application.Common.Time;

/// <summary>
/// AGENTS §6: no DateTime.UtcNow in Domain or Application; time is injected.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
