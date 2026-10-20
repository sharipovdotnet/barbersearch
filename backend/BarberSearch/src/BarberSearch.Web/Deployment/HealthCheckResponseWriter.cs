using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BarberSearch.Web.Deployment;

/// <summary>
/// JSON-ответ health-эндпоинтов.
/// </summary>
/// <remarks>
/// Отдаются только статус, имя проверки и длительность. Описания и исключения не выводятся:
/// readiness-проба публичная (анонимная), а внутри исключения может оказаться строка подключения.
/// </remarks>
public static class HealthCheckResponseWriter
{
    /// <summary>
    /// Пишет компактный JSON в ответ.
    /// </summary>
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store, no-cache";

        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = (int)entry.Value.Duration.TotalMilliseconds
            })
        };

        return context.Response.WriteAsJsonAsync(payload);
    }
}
