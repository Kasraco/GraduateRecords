using Serilog.Context;

namespace GraduateRecords.Web.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    public static string GetCorrelationId(HttpContext context)
    {
        if (context.Items.TryGetValue(ItemKey, out var value) && value is string existing && !string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        return context.TraceIdentifier;
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var incoming)
            && !string.IsNullOrWhiteSpace(incoming))
        {
            var candidate = incoming.ToString().Trim();
            if (candidate.Length <= 128 && candidate.All(static c => char.IsLetterOrDigit(c) || c is '-' or '_'))
            {
                return candidate;
            }
        }

        return Guid.NewGuid().ToString("N");
    }
}
