using GraduateRecords.Infrastructure.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace GraduateRecords.Tests.Unit;

public sealed class SafeLoggingTests
{
    [Fact]
    public void SafeLogSink_strips_exception_message_and_stack_trace_but_keeps_the_type()
    {
        var capture = new CapturingSink();
        var inner = new LoggerConfiguration().WriteTo.Sink(capture).CreateLogger();
        var logger = new LoggerConfiguration().WriteTo.Sink(new SafeLogSink(inner)).CreateLogger();

        try
        {
            throw new InvalidOperationException("connection string secret=abc123");
        }
        catch (Exception error)
        {
            logger.Error(error, "Unhandled exception {ExceptionType}", error.GetType().FullName);
        }

        var emitted = Assert.Single(capture.Events);
        Assert.Null(emitted.Exception);
        Assert.DoesNotContain("secret", emitted.RenderMessage(), StringComparison.OrdinalIgnoreCase);
        Assert.True(emitted.Properties.ContainsKey("ExceptionType"));
        Assert.Equal(
            typeof(InvalidOperationException).FullName,
            ((ScalarValue)emitted.Properties["ExceptionType"]).Value);
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
