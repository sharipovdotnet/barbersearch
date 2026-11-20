using System.Net;
using BarberSearch.Mobile.Core.Localization;

namespace BarberSearch.Mobile.Core.Api.Account;

/// <summary>
/// Тексты отказов аватара: доменные коды и статус важнее английского <c>detail</c> сервера.
/// </summary>
/// <remarks>
/// <para>
/// Порядок проверок повторяет веб (<c>frontend/src/lib/avatars.ts</c>): сначала <c>413</c>
/// без ProblemDetails (Kestrel обрывает запрос до действия), затем коды файла — включая
/// общие с портфолио <c>barberProfile.uploadPhoto.*</c> — затем сбой хранилища и только
/// потом <c>401</c>, <c>403</c>, <c>429</c>, <c>5xx</c> и транспорт. Иначе общий <c>401</c>
/// перекрыл бы точный код «файл слишком большой», а неизвестный маршрут — причину отказа.
/// </para>
/// <para>
/// Английский <c>detail</c> не показывается никогда: его заменяет русский фолбэк. Строки
/// читаются через <see cref="Loc.T"/> при каждом обращении, а не при загрузке типа: язык мог
/// смениться, пока экран жив, и закэшированный текст остался бы на прежнем языке.
/// </para>
/// </remarks>
public static class AvatarErrors
{
    private const string UserFileRequiredCode = "user.uploadAvatar.fileRequired";
    private const string PortfolioFileRequiredCode = "barberProfile.uploadPhoto.fileRequired";
    private const string EmptyImageCode = "barberProfile.uploadPhoto.empty";
    private const string UserTooLargeCode = "user.uploadAvatar.tooLarge";
    private const string PortfolioTooLargeCode = "barberProfile.uploadPhoto.tooLarge";
    private const string UnsupportedTypeCode = "barberProfile.uploadPhoto.unsupportedType";
    private const string InvalidImageCode = "barberProfile.uploadPhoto.invalidImage";
    private const string DimensionsCode = "barberProfile.uploadPhoto.dimensions";
    private const string StorageCode = "user.uploadAvatar.storage";

    private static string FileTooLargeText => Loc.T("Файл слишком большой. Максимум — 5 МБ.");
    private static string FileRequiredText => Loc.T("Выберите файл.");
    private static string EmptyImageText => Loc.T("Изображение пустое или слишком маленькое. Выберите другой файл.");
    private static string UnsupportedTypeText => Loc.T("Поддерживаются только JPEG, PNG и WebP.");
    private static string InvalidImageText => Loc.T("Не удалось прочитать изображение. Выберите другой файл.");
    private static string DimensionsText => Loc.T("Стороны фото должны быть от 300 до 6000 px.");
    private static string StorageText => Loc.T("Не удалось сохранить файл. Попробуйте ещё раз.");
    private static string SessionExpiredText => Loc.T("Сеанс истёк. Войдите заново.");
    private static string ForbiddenText => Loc.T("Нет доступа к аватару.");
    private static string TooManyRequestsText => Loc.T("Слишком много запросов. Подождите минуту и повторите.");
    private static string ServerFailedText => Loc.T("Сервер не справился. Повторите попытку.");
    private static string NetworkText => Loc.T("Нет связи с сервером. Проверьте подключение и повторите попытку.");
    private static string FallbackText => Loc.T("Не удалось изменить аватар. Попробуйте ещё раз.");

    /// <summary>
    /// Разбирает исключение загрузки или удаления аватара.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Возвращает готовую строку для человека, а не код: английский <c>detail</c> сервера
    /// наружу не попадает. Парные коды (<c>user.uploadAvatar.*</c> и
    /// <c>barberProfile.uploadPhoto.*</c>) дают один и тот же текст — человеку важно, что
    /// случилось, а не какой из двух контроллеров ответил.
    /// </para>
    /// <para>
    /// Отмена вызова сюда не попадает: ViewModel ловит <see cref="OperationCanceledException"/>
    /// с отменённым токеном отдельно и молчит. <see cref="TaskCanceledException"/> без отмены
    /// токена — это таймаут <see cref="HttpClient.Timeout"/>, поэтому он считается потерей
    /// связи, как и <see cref="HttpRequestException"/>.
    /// </para>
    /// </remarks>
    /// <param name="exception">Исключение, пришедшее из API или транспорта.</param>
    /// <returns>Готовая русская строка для показа на экране.</returns>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is BarberSearchApiException apiException)
        {
            if (apiException.StatusCode == HttpStatusCode.RequestEntityTooLarge)
            {
                return FileTooLargeText;
            }

            if (HasCode(apiException, UserFileRequiredCode)
                || HasCode(apiException, PortfolioFileRequiredCode))
            {
                return FileRequiredText;
            }

            if (HasCode(apiException, EmptyImageCode))
            {
                return EmptyImageText;
            }

            if (HasCode(apiException, UserTooLargeCode)
                || HasCode(apiException, PortfolioTooLargeCode))
            {
                return FileTooLargeText;
            }

            if (HasCode(apiException, UnsupportedTypeCode))
            {
                return UnsupportedTypeText;
            }

            if (HasCode(apiException, InvalidImageCode))
            {
                return InvalidImageText;
            }

            if (HasCode(apiException, DimensionsCode))
            {
                return DimensionsText;
            }

            if (HasCode(apiException, StorageCode))
            {
                return StorageText;
            }

            if (apiException.StatusCode == HttpStatusCode.Unauthorized)
            {
                return SessionExpiredText;
            }

            if (apiException.StatusCode == HttpStatusCode.Forbidden)
            {
                return ForbiddenText;
            }

            if (apiException.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return TooManyRequestsText;
            }

            if ((int)apiException.StatusCode >= 500)
            {
                return ServerFailedText;
            }
        }

        return IsTransportFailure(exception) ? NetworkText : FallbackText;
    }

    /// <summary>
    /// Проверяет доменный код в ответе.
    /// </summary>
    /// <remarks>
    /// Сравнение ordinal: коды — идентификаторы контракта, а не текст для человека; смена
    /// регистра сделала бы код неопознанным, а не «почти тем же».
    /// </remarks>
    private static bool HasCode(BarberSearchApiException exception, string code) =>
        exception.ErrorCodes.Contains(code, StringComparer.Ordinal);

    /// <summary>
    /// Запрос не дошёл до сервера: сеть, таймаут или оборванное соединение.
    /// </summary>
    private static bool IsTransportFailure(Exception exception) =>
        exception is HttpRequestException
            or TaskCanceledException
            or TimeoutException;
}
