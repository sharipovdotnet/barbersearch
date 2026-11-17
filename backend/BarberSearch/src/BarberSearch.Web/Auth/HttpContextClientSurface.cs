using BarberSearch.Application.Abstractions;

namespace BarberSearch.Web.Auth;

/// <summary>
/// <see cref="IClientSurface"/> поверх заголовка <c>X-Client-Surface</c>.
/// </summary>
/// <remarks>
/// <para>
/// Значение сравнивается без учёта регистра и ровно с <c>app</c>. Других значений нет:
/// будущее iOS-приложение тоже приложение и присылает <c>app</c>. Заголовок не валидируется
/// и не логируется — он диагностический, а не контракт: неизвестное значение означает
/// «другая поверхность», и сломанный клиент не должен терять уведомления (AGENT.md §20.11).
/// </para>
/// <para>
/// Имя заголовка — константа здесь, а не россыпь литералов по контроллерам: слой Application
/// о заголовке не знает, поэтому единственное место, где он читается, — этот класс.
/// </para>
/// </remarks>
public sealed class HttpContextClientSurface : IClientSurface
{
    /// <summary>
    /// Имя заголовка, которым мобильное приложение помечает изменение брони.
    /// </summary>
    public const string HEADER_NAME = "X-Client-Surface";

    /// <summary>
    /// Единственное распознаваемое значение заголовка.
    /// </summary>
    public const string MOBILE_APP_VALUE = "app";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextClientSurface(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);

        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public bool IsMobileApp
        => string.Equals(
            _httpContextAccessor.HttpContext?.Request.Headers[HEADER_NAME].ToString(),
            MOBILE_APP_VALUE,
            StringComparison.OrdinalIgnoreCase);
}
