using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using BarberSearch.Contracts.Common;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BarberSearch.Web.Deployment;

/// <summary>
/// Настройки ограничения частоты запросов.
/// </summary>
/// <remarks>
/// <para>
/// Три лимита с окном в минуту и очередью <c>0</c> (лишний запрос получает <c>429</c> сразу,
/// а не ждёт в очереди):
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>auth</c> — 10 запросов в минуту на адрес клиента для входа, регистрации, refresh,
/// logout (выхода) и смены пароля, а также остальных действий <c>AuthController</c>.
/// Партиция по IP остаётся прежней: <c>Connection.RemoteIpAddress</c> после
/// <c>UseForwardedHeaders</c>.
/// </description></item>
/// <item><description>
/// <c>reads</c> — 120 запросов в минуту на адрес клиента для публичного чтения витрины:
/// поиска, карточки барбера, слотов и списка отзывов (R-25). Партиция только по IP, без
/// <c>sub</c>: лимит одинаков для вошедшего и анонимного клиента, иначе вход в аккаунт
/// обходил бы квоту. Адрес — <c>Connection.RemoteIpAddress</c> после
/// <c>UseForwardedHeaders</c>.
/// </description></item>
/// <item><description>
/// Глобальный лимит записи — 30 запросов в минуту на аутентифицированного клиента (claim
/// <c>sub</c>), а без него — на адрес; <c>GET</c>/<c>HEAD</c>/<c>OPTIONS</c>/<c>TRACE</c>
/// не ограничиваются. Он закрывает <c>POST api/bookings</c> и <c>POST api/reviews</c>
/// (каждая бронь берёт advisory lock и занимает слот) без атрибутов на десятках действий.
/// </description></item>
/// </list>
/// <para>
/// На auth-эндпоинтах оба лимита действуют одновременно: 10/мин строже 30/мин, поэтому
/// поведение входа не меняется. Ключ партиции — <c>sub</c> для аутентифицированного клиента,
/// иначе <c>RemoteIpAddress</c>, иначе <c>unknown</c>. Лимитеры живут в памяти процесса:
/// при нескольких инстансах каждый считает запросы отдельно.
/// <para>
/// <c>UseRateLimiter</c> обязан идти после <c>UseAuthentication</c>: ключ записи —
/// claim <c>sub</c> (<see cref="WritePartitionKey"/>), а до аутентификации его нет. Перенос
/// лимитера выше auth превратил бы квоту 30/мин в общую на IP и вернул бы проблему R-22;
/// это осознанное решение R-53, порядок зафиксирован <c>RateLimitOrderTests</c>.
/// </para>
/// </para>
/// </remarks>
public static class RateLimitSettings
{
    /// <summary>
    /// Имя политики для auth-эндпоинтов (<c>[EnableRateLimiting("auth")]</c>).
    /// </summary>
    public const string AuthPolicyName = "auth";

    /// <summary>
    /// Лимит auth-политики: попыток в минуту на адрес клиента.
    /// </summary>
    public const int AuthPermitLimit = 10;

    /// <summary>
    /// Имя политики публичного чтения (<c>[EnableRateLimiting("reads")]</c>).
    /// </summary>
    public const string ReadsPolicyName = "reads";

    /// <summary>
    /// Лимит публичного чтения: запросов в минуту на адрес клиента.
    /// </summary>
    public const int ReadPermitLimit = 120;

    /// <summary>
    /// Лимит записи: запросов в минуту на пользователя (или адрес, если пользователя нет).
    /// </summary>
    public const int WritePermitLimit = 30;

    /// <summary>
    /// Окно всех лимитов.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Ключ партиции для запроса без <c>sub</c> и без <c>RemoteIpAddress</c>.
    /// </summary>
    public const string UnknownPartitionKey = "unknown";

    private const string UserPartitionPrefix = "user:";
    private const string IpPartitionPrefix = "ip:";
    private const string NoLimitPartitionKey = "no-limit";

    /// <summary>
    /// Применяет настройки к параметрам middleware rate limiting.
    /// </summary>
    /// <param name="options">Параметры, которые регистрирует <c>AddRateLimiter</c>.</param>
    /// <remarks>
    /// <c>GlobalLimiter</c> проверяется до политики эндпоинта, поэтому запрос записи сначала
    /// расходует общий лимит 30/мин, а затем — свой более строгий лимит, если он есть.
    /// </remarks>
    public static void Configure(RateLimiterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddPolicy(AuthPolicyName, AuthPartition);
        options.AddPolicy(ReadsPolicyName, ReadsPartition);

        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(WritePartition);
        options.OnRejected = OnRejected;
    }

    /// <summary>
    /// Партиция auth-политики: 10 попыток в минуту на адрес клиента.
    /// </summary>
    public static RateLimitPartition<string> AuthPartition(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: AuthPartitionKey(context),
            factory: _ => FixedWindow(AuthPermitLimit));
    }

    /// <summary>
    /// Партиция политики публичного чтения: 120 запросов в минуту на адрес клиента.
    /// </summary>
    /// <remarks>
    /// Ключ — только IP, без <c>sub</c>: эндпоинты анонимные, и лимит должен одинаково
    /// работать для вошедшего и невошедшего клиента. Адрес берётся из
    /// <c>Connection.RemoteIpAddress</c> уже после <c>UseForwardedHeaders</c>.
    /// </remarks>
    public static RateLimitPartition<string> ReadsPartition(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: IpPartitionKey(context),
            factory: _ => FixedWindow(ReadPermitLimit));
    }

    /// <summary>
    /// Партиция глобального лимита: запись ограничена, безопасные методы — нет.
    /// </summary>
    /// <remarks>
    /// Безопасные методы получают <see cref="RateLimitPartition.GetNoLimiter{TKey}"/>:
    /// чтение карточек, слотов, поиска и списков не должно упираться в лимит записи.
    /// </remarks>
    public static RateLimitPartition<string> WritePartition(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsWriteMethod(context.Request.Method))
        {
            return RateLimitPartition.GetNoLimiter(NoLimitPartitionKey);
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: WritePartitionKey(context),
            factory: _ => FixedWindow(WritePermitLimit));
    }

    /// <summary>
    /// Ключ auth-партиции: адрес клиента или <see cref="UnknownPartitionKey"/>.
    /// </summary>
    /// <remarks>
    /// Оставлен отдельным именем для auth-политики; значение совпадает с
    /// <see cref="IpPartitionKey"/>.
    /// </remarks>
    public static string AuthPartitionKey(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return IpPartitionKey(context);
    }

    /// <summary>
    /// Ключ партиции по адресу клиента: <c>RemoteIpAddress</c> или
    /// <see cref="UnknownPartitionKey"/>.
    /// </summary>
    public static string IpPartitionKey(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Connection.RemoteIpAddress?.ToString() ?? UnknownPartitionKey;
    }

    /// <summary>
    /// Ключ партиции записи: пользователь, затем адрес, затем <see cref="UnknownPartitionKey"/>.
    /// </summary>
    /// <remarks>
    /// <c>sub</c> имеет приоритет: за одним IP могут сидеть разные клиенты (NAT, офис), и
    /// наоборот — один клиент может менять адрес. Префиксы не дают ключу пользователя
    /// совпасть с ключом адреса.
    /// </remarks>
    public static string WritePartitionKey(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var subject = context.User.FindFirstValue(ClaimsPrincipalFactory.SUB_CLAIM);
        if (!string.IsNullOrWhiteSpace(subject))
        {
            return UserPartitionPrefix + subject;
        }

        return context.Connection.RemoteIpAddress is { } address
            ? IpPartitionPrefix + address
            : UnknownPartitionKey;
    }

    /// <summary>
    /// Относится ли метод к небезопасным (изменяющим состояние) методам.
    /// </summary>
    /// <param name="method">Метод HTTP-запроса.</param>
    public static bool IsWriteMethod(string method)
    {
        ArgumentNullException.ThrowIfNull(method);

        return HttpMethods.IsPost(method)
            || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method)
            || HttpMethods.IsDelete(method);
    }

    /// <summary>
    /// Относится ли метод к небезопасным (изменяющим состояние) методам.
    /// </summary>
    /// <param name="method">Метод HTTP-запроса.</param>
    public static bool IsWriteMethod(HttpMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);

        return IsWriteMethod(method.Method);
    }

    /// <summary>
    /// Ответ на отклонённый запрос: <c>429</c> с <see cref="ProblemDetails"/>,
    /// заголовком <c>Retry-After</c> и кодом <see cref="ApiErrorCodes.Common.RateLimitExceeded"/>.
    /// </summary>
    /// <remarks>
    /// Без этого обработчика клиент получил бы пустое тело с одним кодом состояния: остальные
    /// ошибки API приходят как <c>ProblemDetails</c> с расширением <c>errorCodes</c>
    /// (см. <see cref="AuthProblemDetails"/>). <c>Retry-After</c> отдаёт сам лимитер, если
    /// окно фиксированное; значение — секунды до его сброса.
    /// </remarks>
    public static async ValueTask OnRejected(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;

        if (!httpContext.Response.HasStarted)
        {
            httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

            if (context.Lease is not null
                && context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                && retryAfter > TimeSpan.Zero)
            {
                var seconds = (long)Math.Ceiling(retryAfter.TotalSeconds);
                httpContext.Response.Headers.RetryAfter =
                    seconds.ToString(CultureInfo.InvariantCulture);
            }
        }

        var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();

        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests.",
                Detail = "The request rate limit has been exceeded. Retry after the delay from the Retry-After header.",
                Extensions =
                {
                    [FailureProblemDetails.ERROR_CODES_EXTENSION] =
                        new[] { ApiErrorCodes.Common.RateLimitExceeded }
                }
            }
        });
    }

    private static FixedWindowRateLimiterOptions FixedWindow(int permitLimit) => new()
    {
        PermitLimit = permitLimit,
        Window = Window,
        QueueLimit = 0
    };
}
