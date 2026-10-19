using System.Text.Encodings.Web;
using GraduateRecords.Web.Middleware;
using Microsoft.AspNetCore.Diagnostics;

namespace GraduateRecords.Web.Infrastructure;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var correlationId = CorrelationIdMiddleware.GetCorrelationId(httpContext);

        logger.LogError(
            "Unhandled exception {ExceptionType}. CorrelationId={CorrelationId}",
            exception.GetType().FullName,
            correlationId);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        if (WantsJson(httpContext))
        {
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            await httpContext.Response.WriteAsJsonAsync(
                new
                {
                    message = "خطایی رخ داده است.",
                    correlationId,
                },
                cancellationToken);
            return true;
        }

        httpContext.Response.ContentType = "text/html; charset=utf-8";
        var encodedId = HtmlEncoder.Default.Encode(correlationId);
        var html =
            "<!DOCTYPE html>" +
            "<html lang=\"fa\" dir=\"rtl\">" +
            "<head>" +
            "<meta charset=\"utf-8\" />" +
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />" +
            "<title>خطا</title>" +
            "<link rel=\"stylesheet\" href=\"/css/app.css\" />" +
            "</head>" +
            "<body class=\"min-h-screen bg-background text-foreground\">" +
            "<main class=\"mx-auto max-w-xl p-6\">" +
            "<h1 class=\"text-2xl font-bold\">خطا</h1>" +
            "<p class=\"mt-4 text-base leading-7\">خطایی رخ داده است.</p>" +
            $"<p class=\"mt-2 text-base leading-7\">کد پیگیری: <span dir=\"ltr\">{encodedId}</span></p>" +
            "</main>" +
            "</body>" +
            "</html>";

        await httpContext.Response.WriteAsync(html, cancellationToken);
        return true;
    }

    private static bool WantsJson(HttpContext context)
    {
        var accept = context.Request.Headers.Accept.ToString();
        return accept.Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }
}
