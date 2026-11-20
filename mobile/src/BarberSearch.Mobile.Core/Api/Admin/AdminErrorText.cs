using System.Net;
using BarberSearch.Mobile.Core.Localization;

namespace BarberSearch.Mobile.Core.Api.Admin;

/// <summary>
/// Тексты сбоев админских экранов: общие для всех разделов.
/// </summary>
/// <remarks>
/// <para>
/// Один набор на всю админку, в отличие от клиентских экранов, где каждый ViewModel держит
/// свой: разделы администрирования пишут одни и те же слова, и расхождение здесь читалось бы
/// как два разных продукта. Английский <c>detail</c> сервера не показывается никогда —
/// человеку нужен перевод, а не код ответа.
/// </para>
/// <para>
/// Тексты читаются при обращении, а не при загрузке типа: язык мог смениться, а ViewModel
/// создаётся заново при перестройке Shell — оба порядка дают один результат.
/// </para>
/// </remarks>
public static class AdminErrorText
{
    /// <summary>
    /// Нет ответа: сеть, таймаут или сервер недоступен.
    /// </summary>
    public static string Network => Loc.T(
        "Нет связи с сервером. Проверьте подключение и повторите попытку.");

    /// <summary>
    /// Сервер просит подождать: ограничитель частоты.
    /// </summary>
    public static string TooManyRequests => Loc.T(
        "Слишком много запросов. Подождите минуту и повторите.");

    /// <summary>
    /// Роль не администратор: данные не показываются, повторять нечего.
    /// </summary>
    public static string Forbidden => Loc.T("Раздел доступен только администратору");

    /// <summary>
    /// Пояснение к отказу в доступе.
    /// </summary>
    public static string ForbiddenBody => Loc.T(
        "У вашей учётной записи нет прав администратора. Если доступ нужен, обратитесь к владельцу сервиса.");

    /// <summary>
    /// Сессии больше нет: сервер ответил <c>401</c>, пары токенов стёрты.
    /// </summary>
    public static string SessionExpired => Loc.T("Сеанс истёк. Войдите заново.");

    /// <summary>
    /// Текст сбоя чтения по виду отказа и запасной строке экрана.
    /// </summary>
    /// <param name="exception">Исключение, пришедшее из API.</param>
    /// <param name="kind">Вид сбоя из <see cref="ApiFailureClassifier"/>.</param>
    /// <param name="fallback">Строка раздела, если причина не уточняется.</param>
    /// <returns>Текст для человека.</returns>
    public static string Read(Exception exception, ApiFailureKind kind, string fallback) =>
        kind == ApiFailureKind.Transport
            ? Network
            : exception is BarberSearchApiException apiException
                ? ByStatus(apiException.StatusCode, fallback)
                : fallback;

    /// <summary>
    /// Текст сбоя действия по запасной строке раздела.
    /// </summary>
    /// <param name="exception">Исключение, пришедшее из API.</param>
    /// <param name="fallback">Строка раздела, если причина не уточняется.</param>
    /// <returns>Текст для человека.</returns>
    public static string Write(BarberSearchApiException exception, string fallback)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return ByStatus(exception.StatusCode, fallback);
    }

    /// <summary>
    /// Уточняет текст по коду ответа, который человеку что-то меняет.
    /// </summary>
    private static string ByStatus(HttpStatusCode statusCode, string fallback) => statusCode switch
    {
        HttpStatusCode.TooManyRequests => TooManyRequests,
        HttpStatusCode.Forbidden => Forbidden,
        HttpStatusCode.Unauthorized => SessionExpired,
        _ => fallback,
    };
}
