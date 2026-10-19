using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GraduateRecords.Web.Infrastructure;

internal static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Writes status only — never connection strings, paths, exception text, or check details.
    /// </summary>
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status switch
            {
                HealthStatus.Healthy => "Healthy",
                HealthStatus.Degraded => "Degraded",
                _ => "Unhealthy",
            },
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
