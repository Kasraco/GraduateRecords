using GraduateRecords.Application.Common.Time;

namespace GraduateRecords.Infrastructure.Time;

/// <summary>System clock behind the Application <see cref="IClock"/> port.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
