using System.Net;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.Media;

namespace BarberSearch.Mobile.Core.Api.Barber;

/// <summary>
/// Поле формы, у которого показывается отказ действия с фотографией.
/// </summary>
/// <remarks>
/// Ошибка файла относится к выбору кадра, ошибка подписи — к текстовому полю, а всё
/// остальное (лимит, порядок, доступ, сеть) — к общей строке над списком. Разделение
/// повторяет веб (<c>frontend/src/lib/portfolio-photos.ts</c>): человек должен видеть
/// причину там, где он может её исправить.
/// </remarks>
public enum PortfolioPhotoField
{
    /// <summary>
    /// Общая ошибка действия: лимит, порядок, доступ или сеть.
    /// </summary>
    None,

    /// <summary>
    /// Ошибка выбранного файла.
    /// </summary>
    File,

    /// <summary>
    /// Ошибка подписи.
    /// </summary>
    Description,
}

/// <summary>
/// Разобранный отказ действия с фотографией: где показать текст и стоит ли повторять.
/// </summary>
/// <param name="Field">Поле формы, у которого показывается причина.</param>
/// <param name="Message">Готовая строка для человека.</param>
/// <param name="CanRetry">
/// Данные на сервере не изменились, и повтор запроса безопасен: сеть, таймаут, <c>429</c>
/// или <c>5xx</c>. У отказов валидации и доступа повтора нет — он повторит тот же отказ.
/// </param>
public sealed record PortfolioPhotoFailure(
    PortfolioPhotoField Field,
    string Message,
    bool CanRetry);

/// <summary>
/// Тексты отказов портфолио: коды домена важнее английского <c>detail</c> сервера.
/// </summary>
/// <remarks>
/// <para>
/// Один разбор на загрузку, подпись, порядок и публикацию: сервер отвечает одними и теми
/// же кодами, а экран должен показать причину у того поля, которое человек может исправить.
/// Порядок проверок повторяет веб: сначала <c>413</c> и коды полей, затем лимит и порядок,
/// затем <c>403</c>/<c>404</c>, и только потом общие <c>401</c>, <c>429</c>, <c>5xx</c> и
/// транспорт. Иначе, например, <c>404</c> от неизвестного маршрута перекрыл бы точный код
/// «фото не найдено».
/// </para>
/// <para>
/// Строки читаются через <see cref="Loc.T"/> при каждом обращении, а не при загрузке типа:
/// язык мог смениться, а ViewModel создаётся заново при перестройке Shell — оба порядка
/// дают один результат.
/// </para>
/// </remarks>
public static class PortfolioPhotoErrors
{
    private const string FileTooLargeCode = "barberProfile.uploadPhoto.tooLarge";
    private const string FileRequiredCode = "barberProfile.uploadPhoto.fileRequired";
    private const string UnsupportedTypeCode = "barberProfile.uploadPhoto.unsupportedType";
    private const string InvalidImageCode = "barberProfile.uploadPhoto.invalidImage";
    private const string DimensionsCode = "barberProfile.uploadPhoto.dimensions";
    private const string DescriptionCode = "barberProfile.addPhoto.description";

    private static string FileTooLargeText => Loc.T("Файл слишком большой. Максимум — 5 МБ.");
    private static string FileRequiredText => Loc.T("Выберите файл.");
    private static string UnsupportedTypeText => Loc.T("Поддерживаются только JPEG, PNG и WebP.");
    private static string InvalidImageText => Loc.T("Не удалось прочитать изображение. Выберите другой файл.");
    private static string DimensionsText => Loc.T("Стороны фото должны быть от 300 до 6000 px.");
    private static string DescriptionTooLongText => Loc.T("Подпись не длиннее 2000 символов.");
    private static string LimitText => Loc.T("В портфолио уже 10 фото. Удалите одно, чтобы добавить новое.");
    private static string ReorderText => Loc.T("Не удалось сохранить порядок. Обновите страницу.");
    private static string NotFoundText => Loc.T("Фото не найдено. Обновите страницу.");
    private static string ForbiddenText => Loc.T("Нет доступа к портфолио этого барбера.");
    private static string StorageText => Loc.T("Не удалось сохранить файл. Попробуйте ещё раз.");
    private static string SessionExpiredText => Loc.T("Сеанс истёк. Войдите заново.");
    private static string TooManyRequestsText => Loc.T("Слишком много запросов. Подождите минуту и повторите.");
    private static string ServerFailedText => Loc.T("Сервер не справился. Повторите попытку.");
    private static string NetworkText => Loc.T("Нет связи с сервером. Проверьте подключение и повторите попытку.");
    private static string FallbackText => Loc.T("Не удалось выполнить действие с фото. Попробуйте ещё раз.");

    /// <summary>
    /// Разбирает исключение действия с фотографией.
    /// </summary>
    /// <remarks>
    /// Отмена вызова сюда не попадает: ViewModel ловит <see cref="OperationCanceledException"/>
    /// с отменённым токеном отдельно и молчит. <see cref="TaskCanceledException"/> без отмены
    /// токена — это таймаут <see cref="HttpClient.Timeout"/>, поэтому он считается потерей
    /// связи, как и <see cref="HttpRequestException"/>.
    /// </remarks>
    /// <param name="exception">Исключение, пришедшее из API или транспорта.</param>
    /// <returns>Поле, текст и признак безопасного повтора.</returns>
    public static PortfolioPhotoFailure Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is BarberSearchApiException apiException)
        {
            if (apiException.StatusCode == HttpStatusCode.RequestEntityTooLarge)
            {
                return FileFailure(FileTooLargeText);
            }

            if (HasCode(apiException, FileRequiredCode))
            {
                return FileFailure(FileRequiredText);
            }

            if (HasCode(apiException, FileTooLargeCode))
            {
                return FileFailure(FileTooLargeText);
            }

            if (HasCode(apiException, UnsupportedTypeCode))
            {
                return FileFailure(UnsupportedTypeText);
            }

            if (HasCode(apiException, InvalidImageCode))
            {
                return FileFailure(InvalidImageText);
            }

            if (HasCode(apiException, DimensionsCode))
            {
                return FileFailure(DimensionsText);
            }

            if (HasCode(apiException, DescriptionCode))
            {
                return new PortfolioPhotoFailure(
                    PortfolioPhotoField.Description,
                    DescriptionTooLongText,
                    CanRetry: false);
            }

            if (HasCode(apiException, "barberProfile.addPhoto.limit"))
            {
                return GeneralFailure(LimitText, canRetry: false);
            }

            if (HasCode(apiException, "barberProfile.reorderPhotos.invalid"))
            {
                return GeneralFailure(ReorderText, canRetry: false);
            }

            if (HasCode(apiException, "portfolioPhoto.block.notFound")
                || HasCode(apiException, "portfolioPhoto.unblock.notFound")
                || apiException.StatusCode == HttpStatusCode.NotFound)
            {
                return GeneralFailure(NotFoundText, canRetry: false);
            }

            if (apiException.StatusCode == HttpStatusCode.Forbidden
                || HasCode(apiException, "auth.forbidden"))
            {
                return GeneralFailure(ForbiddenText, canRetry: false);
            }

            if (HasCode(apiException, "barberProfile.uploadPhoto.storage"))
            {
                return GeneralFailure(StorageText, canRetry: true);
            }

            if (apiException.StatusCode == HttpStatusCode.Unauthorized)
            {
                return GeneralFailure(SessionExpiredText, canRetry: false);
            }

            if (apiException.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // Данные не изменились, лимит снимется сам: повтор безопасен.
                return GeneralFailure(TooManyRequestsText, canRetry: true);
            }

            if ((int)apiException.StatusCode >= 500)
            {
                return GeneralFailure(ServerFailedText, canRetry: true);
            }
        }

        return IsTransportFailure(exception)
            ? GeneralFailure(NetworkText, canRetry: true)
            : GeneralFailure(FallbackText, canRetry: false);
    }

    /// <summary>
    /// Переводит причину подготовки изображения в текст у поля «Файл».
    /// </summary>
    /// <remarks>
    /// Пикер не знает языка интерфейса и отдаёт только машинный код; строка выбирается
    /// здесь, в Core, где доступен каталог перевода. Повторять подготовку бессмысленно:
    /// тот же файл даст ту же причину, поэтому <c>CanRetry</c> = <c>false</c>.
    /// </remarks>
    /// <param name="kind">Причина отказа пикера.</param>
    /// <returns>Поле, текст и признак повтора.</returns>
    public static PortfolioPhotoFailure DescribePicker(PortfolioImageError kind) => kind switch
    {
        PortfolioImageError.SourceTooLarge => FileFailure(
            Loc.T("Файл слишком большой. Максимум — 15 МБ.")),
        PortfolioImageError.UnsupportedType => FileFailure(UnsupportedTypeText),
        PortfolioImageError.InvalidImage => FileFailure(InvalidImageText),
        _ => FileFailure(
            Loc.T("Не удалось уместить фото в 5 МБ. Выберите другое фото.")),
    };

    /// <summary>
    /// Проверяет доменный код в ответе.
    /// </summary>
    private static bool HasCode(BarberSearchApiException exception, string code) =>
        exception.ErrorCodes.Contains(code, StringComparer.Ordinal);

    /// <summary>
    /// Отказ у поля «Файл»: повтор не помогает, файл тот же.
    /// </summary>
    private static PortfolioPhotoFailure FileFailure(string message) =>
        new(PortfolioPhotoField.File, message, CanRetry: false);

    /// <summary>
    /// Общая ошибка над списком.
    /// </summary>
    private static PortfolioPhotoFailure GeneralFailure(string message, bool canRetry) =>
        new(PortfolioPhotoField.None, message, canRetry);

    /// <summary>
    /// Запрос не дошёл до сервера: сеть, таймаут или оборванное соединение.
    /// </summary>
    private static bool IsTransportFailure(Exception exception) =>
        exception is HttpRequestException
            or TaskCanceledException
            or TimeoutException;
}
