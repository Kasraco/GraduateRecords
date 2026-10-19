using Serilog.Core;
using Serilog.Events;

namespace GraduateRecords.Infrastructure.Logging;

/// <summary>
/// Guarantees that no exception instance (message or stack trace) reaches any
/// downstream sink. Only the exception type name is preserved, which satisfies
/// AGENTS §14 ("raw stack traces" are never logged) while keeping operational signal.
/// </summary>
public sealed class SafeLogSink(Serilog.ILogger inner) : ILogEventSink
{
    private const string ExceptionTypeProperty = "ExceptionType";

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Exception is null)
        {
            inner.Write(logEvent);
            return;
        }

        var properties = logEvent.Properties
            .Where(static property => property.Key != ExceptionTypeProperty)
            .Select(static property => new LogEventProperty(property.Key, property.Value))
            .ToList();
        properties.Add(new LogEventProperty(
            ExceptionTypeProperty,
            new ScalarValue(logEvent.Exception.GetType().FullName)));

        inner.Write(new LogEvent(
            logEvent.Timestamp,
            logEvent.Level,
            exception: null,
            logEvent.MessageTemplate,
            properties));
    }
}
