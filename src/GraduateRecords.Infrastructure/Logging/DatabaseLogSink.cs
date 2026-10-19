using System.Data;
using Microsoft.Data.SqlClient;
using Serilog.Core;
using Serilog.Events;

namespace GraduateRecords.Infrastructure.Logging;

/// <summary>
/// Writes structured application logs to the dedicated <c>log</c> schema.
/// Failures are swallowed so the file sink stays the fallback and logging can
/// never take the application down.
/// </summary>
public sealed class DatabaseLogSink : ILogEventSink
{
    private const string InsertCommand =
        "INSERT INTO [log].[ApplicationLog] " +
        "([OccurredAtUtc],[Level],[Message],[ExceptionType],[CorrelationId],[Application]) " +
        "VALUES (@occurredAtUtc,@level,@message,@exceptionType,@correlationId,@application);";

    private readonly string _connectionString;
    private readonly TimeSpan _retryDelay;
    private readonly object _gate = new();
    private DateTimeOffset _nextAttemptUtc = DateTimeOffset.MinValue;

    public DatabaseLogSink(string connectionString, TimeSpan? retryDelay = null)
    {
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(60);
        _connectionString = BuildShortTimeoutConnectionString(connectionString);
    }

    public void Emit(LogEvent logEvent)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return;
        }

        lock (_gate)
        {
            if (DateTimeOffset.UtcNow < _nextAttemptUtc)
            {
                return;
            }
        }

        try
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = InsertCommand;
            command.CommandType = CommandType.Text;
            command.Parameters.Add("@occurredAtUtc", SqlDbType.DateTime2).Value = logEvent.Timestamp.UtcDateTime;
            command.Parameters.Add("@level", SqlDbType.NVarChar, 16).Value = logEvent.Level.ToString();
            command.Parameters.Add("@message", SqlDbType.NVarChar, 4000).Value = Truncate(logEvent.RenderMessage(), 4000);
            command.Parameters.Add("@exceptionType", SqlDbType.NVarChar, 256).Value =
                (object?)logEvent.Exception?.GetType().FullName ?? DBNull.Value;
            command.Parameters.Add("@correlationId", SqlDbType.NVarChar, 64).Value =
                logEvent.Properties.TryGetValue("CorrelationId", out var correlationId)
                    ? Truncate(correlationId.ToString(), 64)
                    : (object)DBNull.Value;
            command.Parameters.Add("@application", SqlDbType.NVarChar, 128).Value =
                logEvent.Properties.TryGetValue("Application", out var application)
                    ? Truncate(application.ToString(), 128)
                    : "GraduateRecords";
            command.ExecuteNonQuery();

            lock (_gate)
            {
                _nextAttemptUtc = DateTimeOffset.MinValue;
            }
        }
        catch
        {
            // Never rethrow: an unavailable log database must not fail the request.
            lock (_gate)
            {
                _nextAttemptUtc = DateTimeOffset.UtcNow.Add(_retryDelay);
            }
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string BuildShortTimeoutConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return string.Empty;
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                ConnectTimeout = 2,
            };
            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }
}
