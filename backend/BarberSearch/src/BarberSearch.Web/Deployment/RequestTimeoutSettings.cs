using Microsoft.AspNetCore.Http.Timeouts;

namespace BarberSearch.Web.Deployment;

/// <summary>
/// Настройки таймаута запроса.
/// </summary>
/// <remarks>
/// 30 секунд — страховка от зависшего запроса (деградация БД, ожидание advisory lock):
/// по истечении отменяется <c>RequestAborted</c>, и клиент получает <c>504</c>.
/// Health-проверки выведены из таймаута: readiness сам ограничен
/// <c>HealthChecks:DatabaseTimeoutSeconds</c> и должен отвечать <c>503</c> при недоступной
/// БД, а не <c>504</c> от общего middleware.
/// </remarks>
public static class RequestTimeoutSettings
{
    /// <summary>
    /// Таймаут по умолчанию в секундах.
    /// </summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>
    /// Таймаут по умолчанию для бизнес-запросов.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds);

    /// <summary>
    /// Таймаут health-проверок: таймаут отключён, проверка ограничена собственным временем.
    /// </summary>
    public static readonly TimeSpan HealthCheckTimeout = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Применяет настройки к параметрам middleware таймаутов.
    /// </summary>
    /// <param name="options">Параметры, которые регистрирует <c>AddRequestTimeouts</c>.</param>
    public static void Configure(RequestTimeoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.DefaultPolicy = new RequestTimeoutPolicy { Timeout = DefaultTimeout };
    }
}
