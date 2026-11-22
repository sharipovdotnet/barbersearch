using System.Net;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Localization;

namespace BarberSearch.Mobile.Core.Formatting;

/// <summary>
/// Тексты формы «Создать салон»: клиентские проверки, подсказки и отказы сервера.
/// </summary>
/// <remarks>
/// <para>
/// Строки повторяют веб (<c>frontend/src/lib/salon-form.ts</c>,
/// <c>components/salon/salon-form.tsx</c>), но живут в Core: страница не собирает тексты
/// сама, а тест проверяет их без разметки. Все они проходят через
/// <see cref="Loc.T(string)"/>, поэтому английский каталог покрывает форму так же, как
/// остальное приложение.
/// </para>
/// <para>
/// Клиентские проверки — только те, что веб делает сам: обязательность полей, диапазон
/// координат, формат сайта, длину телефона и российский мобильный номер. Длину названия,
/// адреса, города и страны сервер не ограничивает — выдумывать лимит нельзя. Часовой пояс
/// выбирается из списка, поэтому свободный ввод проверяется как «пусто».
/// </para>
/// </remarks>
public static class BarberSalonCreateText
{
    /// <summary>
    /// Обязательное название: пустое поле.
    /// </summary>
    public static string NameRequired => Loc.T("Введите название.");

    /// <summary>
    /// Обязательный адрес: пустое поле.
    /// </summary>
    public static string AddressRequired => Loc.T("Введите адрес.");

    /// <summary>
    /// Обязательный город: пустое поле.
    /// </summary>
    public static string CityRequired => Loc.T("Введите город.");

    /// <summary>
    /// Обязательная страна: пустое поле.
    /// </summary>
    public static string CountryRequired => Loc.T("Введите страну.");

    /// <summary>
    /// Обязательный часовой пояс: значение не выбрано.
    /// </summary>
    public static string TimeZoneRequired => Loc.T("Выберите часовой пояс из списка.");

    /// <summary>
    /// Широта вне диапазона или не число.
    /// </summary>
    public static string LatitudeRange => Loc.T("От -90 до 90.");

    /// <summary>
    /// Долгота вне диапазона или не число.
    /// </summary>
    public static string LongitudeRange => Loc.T("От -180 до 180.");

    /// <summary>
    /// Сайт не начинается с <c>http://</c> или <c>https://</c> либо длиннее 2048 символов.
    /// </summary>
    public static string WebsiteInvalid =>
        Loc.T("Адрес сайта начинается с http:// или https://.");

    /// <summary>
    /// Телефон длиннее 32 символов.
    /// </summary>
    public static string PhoneTooLong => Loc.T("Не длиннее 32 символов.");

    /// <summary>
    /// Телефон не является российским мобильным номером.
    /// </summary>
    /// <remarks>
    /// Проверка та же, что у профиля и телефона гостя: её выполняет серверный
    /// <c>PhoneNumber</c>, а текст общий у всех трёх форм.
    /// </remarks>
    public static string PhoneInvalid => PhoneInputText.Invalid;

    /// <summary>
    /// Клиентская ошибка видимости: тип места не может быть в каталоге.
    /// </summary>
    /// <remarks>
    /// Сервер отвечает этим же правилом кодом <c>salon.isListed.kind</c>, но у формы
    /// комбинация предотвращена логикой типа: дом и выезд всегда скрыты. Текст остаётся
    /// в одном месте на случай, если правило понадобится показать у поля.
    /// </remarks>
    public static string ListingUnavailable =>
        Loc.T("Для этого типа места каталог недоступен.");

    /// <summary>
    /// Клиентская ошибка видимости: барбершоп и студию нельзя спрятать.
    /// </summary>
    public static string ListingForced =>
        Loc.T("Барбершоп и студию нельзя скрыть из каталога.");

    /// <summary>
    /// Подсказка адреса до выбора города: поле адреса недоступно.
    /// </summary>
    public static string ChooseCityFirst => Loc.T("Сначала выберите город.");

    /// <summary>
    /// Подсказка переключателя «Показывать в каталоге» — текст веба.
    /// </summary>
    public static string ListingHint => Loc.T(
        "Скрытое место не попадает в каталог и не требует проверки, но остаётся доступным для записи с вашей карточки.");

    /// <summary>
    /// Подсказка про координаты: встроенной карты на телефоне нет.
    /// </summary>
    public static string CoordinatesHint => Loc.T(
        "Скопируйте координаты из карт или выберите подсказку адреса — встроенной карты нет.");

    /// <summary>
    /// Подсказка у названия: после создания его не изменить.
    /// </summary>
    public static string NameHint => Loc.T("Название нельзя изменить после создания салона.");

    /// <summary>
    /// Запасной текст отказа создания салона.
    /// </summary>
    public static string SaveFailed =>
        Loc.T("Не удалось создать салон. Попробуйте ещё раз.");

    /// <summary>
    /// Объявление после создания: публичное место ждёт проверки, скрытое уже работает.
    /// </summary>
    /// <param name="isListed">Уходит ли место в каталог.</param>
    /// <returns>«Салон отправлен на проверку.» или «Место приёма создано.».</returns>
    public static string Created(bool isListed) => isListed
        ? Loc.T("Салон отправлен на проверку.")
        : Loc.T("Место приёма создано.");

    /// <summary>
    /// Ошибки полей формы из ответа сервера.
    /// </summary>
    /// <remarks>
    /// Домен проверяет поля сам, и опечатка в часовом поясе или пустой город приходят
    /// кодом, а не текстом; показывать их нужно у поля, иначе человек ищет причину в общем
    /// сообщении. Несколько кодов в одном ответе заполняют несколько полей — форма не
    /// возвращается после первого.
    /// </remarks>
    /// <param name="exception">Отказ API.</param>
    /// <returns>Тексты по полям; <c>null</c> — поле сервер не назвал.</returns>
    public static SalonCreateFieldErrors FieldErrors(BarberSearchApiException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new SalonCreateFieldErrors(
            Name: HasCode(exception, "salon.name") ? NameRequired : null,
            Address: HasCode(exception, "salon.address") ? AddressRequired : null,
            City: HasCode(exception, "salon.city") || HasCode(exception, "salon.city.invalid")
                ? CityRequired
                : null,
            Country: HasCode(exception, "salon.country") ? CountryRequired : null,
            TimeZoneId: HasCode(exception, "salon.timeZoneId")
                || HasCode(exception, "salon.timeZoneId.unknown")
                ? TimeZoneRequired
                : null,
            WebsiteUrl: HasCode(exception, "salon.websiteUrl") ? WebsiteInvalid : null,
            Latitude: HasCode(exception, "salon.latitude") ? LatitudeRange : null,
            Longitude: HasCode(exception, "salon.longitude") ? LongitudeRange : null,
            IsListed: HasCode(exception, "salon.isListed.kind") ? ListingForced : null,
            PhoneNumber: HasCode(exception, "salon.phoneNumber") ? PhoneInvalid : null);
    }

    /// <summary>
    /// Общий текст отказа создания салона.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Коды и статусы объясняют причину точнее, чем один запасной текст, а серверный
    /// <c>detail</c> не показывается — он английский. <c>403</c> у барбера значит две разные
    /// вещи: «нет профиля мастера» и «это чужой салон», — и обе названы отдельно.
    /// </para>
    /// <para>
    /// Транспортный сбой и таймаут (<see cref="OperationCanceledException"/> при живом
    /// токене) — это «нет связи», а отмена экраном сюда не доходит: её глушит вызывающий.
    /// Записи не повторяются сами: после сбоя исход неизвестен, и человек решает сам.
    /// </para>
    /// </remarks>
    /// <param name="exception">Исключение создания салона.</param>
    /// <returns>Причина для человека.</returns>
    public static string SaveError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is BarberSearchApiException apiException)
        {
            if (apiException.StatusCode == HttpStatusCode.Forbidden)
            {
                return HasCode(apiException, "barberProfile.notFound")
                    ? Loc.T("Профиль барбера не найден. Создайте профиль и повторите.")
                    : Loc.T("Это чужой салон — править его может только владелец.");
            }

            if (apiException.StatusCode == HttpStatusCode.Conflict
                && apiException.ErrorCodes.Any(
                    code => code.EndsWith("update.concurrencyConflict", StringComparison.Ordinal)))
            {
                return Loc.T("Запись изменили одновременно. Обновите список.");
            }

            if (HasCode(apiException, "salon.kind.unknown"))
            {
                return Loc.T(
                    "Неизвестный тип места. Обновите страницу и попробуйте ещё раз.");
            }

            return apiException.StatusCode switch
            {
                HttpStatusCode.TooManyRequests =>
                    Loc.T("Слишком много действий подряд. Подождите минуту."),
                HttpStatusCode.Unauthorized =>
                    Loc.T("Сессия истекла — войдите снова."),
                _ => SaveFailed,
            };
        }

        return exception is HttpRequestException or OperationCanceledException
            ? Loc.T("Нет связи с сервером. Проверьте подключение и повторите попытку.")
            : SaveFailed;
    }

    /// <summary>
    /// Проверяет наличие публичного кода ошибки.
    /// </summary>
    /// <param name="exception">Отказ сервера.</param>
    /// <param name="code">Публичный код.</param>
    /// <returns><c>true</c>, если код есть в ответе.</returns>
    private static bool HasCode(BarberSearchApiException exception, string code) =>
        exception.ErrorCodes.Contains(code, StringComparer.Ordinal);
}

/// <summary>
/// Ошибки полей формы создания салона: по одной строке на поле.
/// </summary>
/// <remarks>
/// <c>null</c> означает, что сервер это поле не назвал, — и тогда ошибка не показывается.
/// Записи, а не класс с изменяемыми свойствами: набор заполняется один раз из ответа, и
/// общая ошибка формы решается по тому, осталось ли хоть одно непустое поле.
/// </remarks>
/// <param name="Name">Ошибка названия.</param>
/// <param name="Address">Ошибка адреса.</param>
/// <param name="City">Ошибка города.</param>
/// <param name="Country">Ошибка страны.</param>
/// <param name="TimeZoneId">Ошибка часового пояса.</param>
/// <param name="WebsiteUrl">Ошибка сайта.</param>
/// <param name="Latitude">Ошибка широты.</param>
/// <param name="Longitude">Ошибка долготы.</param>
/// <param name="IsListed">Ошибка видимости в каталоге.</param>
/// <param name="PhoneNumber">Ошибка телефона.</param>
public sealed record SalonCreateFieldErrors(
    string? Name = null,
    string? Address = null,
    string? City = null,
    string? Country = null,
    string? TimeZoneId = null,
    string? WebsiteUrl = null,
    string? Latitude = null,
    string? Longitude = null,
    string? IsListed = null,
    string? PhoneNumber = null);
