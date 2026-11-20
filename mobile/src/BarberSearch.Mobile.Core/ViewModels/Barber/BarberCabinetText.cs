using System.Globalization;
using System.Net;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Bookings;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Тексты кабинета мастера, общие для его разделов.
/// </summary>
/// <remarks>
/// <para>
/// Формулировки взяты из веба (<c>frontend/src/components/barber/barber-day.tsx</c>,
/// <c>frontend/src/components/barber/cabinet-booking-row.tsx</c>,
/// <c>frontend/src/lib/errors.ts</c>), чтобы два клиента говорили одними словами.
/// Отдельная копия нужна потому, что строки кабинета не совпадают со строками клиентских
/// «Записей»: здесь клиент — не «вы», а визит называет сам мастер.
/// </para>
/// <para>
/// Ошибки чтения и ошибки действий разделены: чтение повторяется само и не должно
/// показывать английский <c>detail</c>, а действие возвращает ещё и признак
/// «строку надо перечитать» (<c>409</c>). Ошибки расписания сюда не входят — это
/// отдельный раздел кабинета.
/// </para>
/// </remarks>
public static class BarberCabinetText
{
    private static string NetworkErrorText => Loc.T(
        "Нет связи с сервером. Проверьте подключение и повторите попытку.");
    private static string TooManyRequestsText => Loc.T(
        "Слишком много запросов. Подождите минуту и повторите.");
    private static string SessionExpiredText => Loc.T("Сессия истекла — войдите снова.");
    private static string RecordsForbiddenText => Loc.T("Нет доступа к записям клиентов.");

    private const string MarkNoShowTooEarlyCode = "booking.markNoShow.tooEarly";
    private const string CreateCustomerCode = "booking.create.customer";
    private const string CreateCustomerAmbiguousCode = "booking.create.customerAmbiguous";
    private const string CreateGuestFirstNameCode = "booking.create.guestFirstName";
    private const string CreateGuestPhoneCode = "booking.create.guestPhone";
    private const string CreateStartAtInPastCode = "booking.create.startAt.inPast";
    private const string CreateStartAtTooFarCode = "booking.create.startAt.tooFar";

    /// <summary>
    /// Остаточная гонка переноса на exclusion-ограничении: значит то же, что и <c>overlap</c>.
    /// </summary>
    /// <remarks>
    /// Не входит в <see cref="ApiErrorCodes.Booking"/>: реестр перечисляет коды операций, а
    /// это код сохранения строки (<c>UpdatePrefix</c> + суффикс ограничения).
    /// </remarks>
    private const string UpdateOverlapCode = "booking.update.overlap";

    /// <summary>
    /// Подпись статуса записи; текст берётся из общего <see cref="BookingText.Status"/>.
    /// </summary>
    /// <param name="status">Статус из ответа API.</param>
    /// <returns>Русская подпись статуса.</returns>
    public static string StatusText(BookingStatus status) => BookingText.Status(status);

    /// <summary>
    /// Машинный вид статуса для класса карточки: <c>pending</c>, <c>confirmed</c>…
    /// </summary>
    /// <remarks>
    /// XAML не умеет ветвить по перечислению в стиле, поэтому строка статуса приходит
    /// готовым ключом, а не сравнивается с <see cref="BookingStatus"/> в разметке.
    /// </remarks>
    /// <param name="status">Статус из ответа API.</param>
    /// <returns>Короткий ключ вида статуса.</returns>
    public static string StatusKind(BookingStatus status) => status switch
    {
        BookingStatus.Pending => "pending",
        BookingStatus.Confirmed => "confirmed",
        BookingStatus.Completed => "completed",
        BookingStatus.Cancelled => "cancelled",
        BookingStatus.NoShow => "noshow",
        _ => "pending",
    };

    /// <summary>
    /// Время визита в поясе салона: «10:00–11:00».
    /// </summary>
    /// <param name="startAt">Начало визита в UTC.</param>
    /// <param name="endAt">Окончание визита в UTC.</param>
    /// <param name="timeZone">Пояс салона; <c>null</c> — UTC.</param>
    /// <returns>Время начала и конца через тире.</returns>
    public static string TimeRange(DateTimeOffset startAt, DateTimeOffset endAt, TimeZoneInfo? timeZone) =>
        SalonClock.FormatTimeRange(startAt, endAt, timeZone);

    /// <summary>
    /// Заголовок дня: «Сегодня», «Завтра» или короткая дата.
    /// </summary>
    /// <remarks>
    /// «Сегодня» и «Завтра» считаются в той же календарной системе, что и день ленты:
    /// мастер смотрит свой телефон, поэтому граница берётся из пояса устройства.
    /// </remarks>
    /// <param name="day">День ленты.</param>
    /// <param name="today">Сегодняшняя дата в поясе устройства.</param>
    /// <returns>Слово для сегодняшнего и завтрашнего дня, иначе короткая дата.</returns>
    public static string DayHeading(DateOnly day, DateOnly today)
    {
        if (day == today)
        {
            return Loc.T("Сегодня");
        }

        return day == today.AddDays(1)
            ? Loc.T("Завтра")
            : BookingText.ShortDate(day);
    }

    /// <summary>
    /// Подпись дня: «сб, 15 ноября · 2 визита · 1 окно».
    /// </summary>
    /// <remarks>
    /// Нулевые части опускаются, как в вебе: «0 окон» — не факт о дне, а шум. Счётчик
    /// визитов склоняется общим <see cref="PluralKey.Visits"/>; для окон форма собирается
    /// здесь, потому что отдельного ключа счётчика в каталоге телефона пока нет.
    /// </remarks>
    /// <param name="day">День ленты.</param>
    /// <param name="visits">Сколько визитов в дне.</param>
    /// <param name="gaps">Сколько окон между визитами.</param>
    /// <returns>Дата и непустые счётчики через среднюю точку.</returns>
    public static string TodaySubtitle(DateOnly day, int visits, int gaps)
    {
        var parts = new List<string>(3) { SalonClock.FormatWeekdayDate(day) };

        if (visits > 0)
        {
            parts.Add(Loc.Plural(PluralKey.Visits, visits));
        }

        if (gaps > 0)
        {
            parts.Add(GapsCount(gaps));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Сводка дня: «2 визита · 4 800 ₽».
    /// </summary>
    /// <param name="visits">Сколько визитов в дне.</param>
    /// <param name="revenue">Сумма визитов дня.</param>
    /// <returns>Счётчик визитов и цена через среднюю точку.</returns>
    public static string Summary(int visits, decimal revenue) => string.Concat(
        Loc.Plural(PluralKey.Visits, visits),
        " · ",
        BarberText.Price(revenue));

    /// <summary>
    /// Строка о ближайшем клиенте.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Идущий визит важнее отсчёта: у него нет «через», и мастеру нужен его конец.
    /// Дальше работает правило веба: до семи дней — обратный отсчёт, дальше — время начала.
    /// </para>
    /// <para>
    /// <c>null</c> означает, что визит уже закончился (или ещё не начался, но и не должен
    /// попасть в строку), и подпись показывать нечего.
    /// </para>
    /// </remarks>
    /// <param name="startAt">Начало визита в UTC.</param>
    /// <param name="endAt">Окончание визита в UTC.</param>
    /// <param name="now">Текущий момент.</param>
    /// <param name="timeZone">Пояс салона; <c>null</c> — UTC.</param>
    /// <returns>Строка «следующий клиент» либо <c>null</c>.</returns>
    public static string? NextClient(
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        DateTimeOffset now,
        TimeZoneInfo? timeZone)
    {
        if (endAt <= now)
        {
            return null;
        }

        if (startAt <= now)
        {
            return Loc.T("Идёт визит до {0}", SalonClock.FormatTime(endAt, timeZone));
        }

        var countdown = BookingText.Countdown(startAt, now);

        return countdown is null
            ? Loc.T("Следующий клиент в {0}", SalonClock.FormatTime(startAt, timeZone))
            : Loc.T("Следующий клиент {0}", countdown);
    }

    /// <summary>
    /// Окно между визитами: «свободно 30 мин».
    /// </summary>
    /// <param name="minutes">Длительность окна в минутах.</param>
    /// <returns>Подпись окна.</returns>
    public static string GapText(int minutes) => Loc.T(
        "свободно {0} мин",
        minutes.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Причина неудачного чтения — по статусу, никогда по английскому <c>detail</c>.
    /// </summary>
    /// <param name="exception">Исключение запроса.</param>
    /// <param name="fallback">Запасной текст раздела.</param>
    /// <returns>Текст для человека.</returns>
    public static string ReadError(Exception? exception, string fallback)
    {
        if (exception is HttpRequestException or OperationCanceledException)
        {
            return NetworkErrorText;
        }

        if (exception is not BarberSearchApiException apiException)
        {
            return fallback;
        }

        return apiException.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => TooManyRequestsText,
            HttpStatusCode.Unauthorized => SessionExpiredText,
            HttpStatusCode.Forbidden => RecordsForbiddenText,
            _ => fallback,
        };
    }

    /// <summary>
    /// Причина неудачного действия с записью.
    /// </summary>
    /// <param name="exception">Исключение запроса.</param>
    /// <param name="fallback">Запасной текст действия.</param>
    /// <returns>Текст для человека.</returns>
    public static string ActionError(Exception? exception, string fallback)
    {
        if (exception is HttpRequestException or OperationCanceledException)
        {
            return NetworkErrorText;
        }

        if (exception is not BarberSearchApiException apiException)
        {
            return fallback;
        }

        if (HasCode(apiException, MarkNoShowTooEarlyCode))
        {
            return Loc.T("Неявку нельзя отметить до начала визита.");
        }

        if (HasCode(apiException, ApiErrorCodes.Booking.BarberNoteTooLong))
        {
            return Loc.T("Слишком длинная заметка — максимум 1000 символов.");
        }

        if (HasCode(apiException, ApiErrorCodes.Booking.CancelReasonRequired))
        {
            return Loc.T("Укажите причину отмены.");
        }

        if (HasCode(apiException, ApiErrorCodes.Booking.CancelReasonTooLong))
        {
            return Loc.T("Слишком длинная причина — максимум 500 символов.");
        }

        if (HasCode(apiException, ApiErrorCodes.Booking.PriceRequired))
        {
            return Loc.T("Укажите цену.");
        }

        if (HasCode(apiException, ApiErrorCodes.Booking.PriceNegative))
        {
            return Loc.T("Цена не может быть отрицательной.");
        }

        if (HasCode(apiException, ApiErrorCodes.Booking.PriceTooLarge))
        {
            return Loc.T("Слишком большая цена — максимум 99 999 999,99 ₽.");
        }

        if (HasCode(apiException, ApiErrorCodes.Booking.PricePrecision))
        {
            return Loc.T("Цена — не больше двух знаков после запятой.");
        }

        if (HasCode(apiException, ApiErrorCodes.Booking.PriceStatus))
        {
            return Loc.T("Цену нельзя изменить у отменённой записи или у неявки.");
        }

        if (apiException.StatusCode == HttpStatusCode.NotFound
            || HasCode(apiException, ApiErrorCodes.Booking.NotFound))
        {
            return Loc.T("Запись не найдена.");
        }

        if (apiException.StatusCode == HttpStatusCode.Forbidden
            || HasCode(apiException, ApiErrorCodes.Auth.Forbidden))
        {
            return Loc.T("Это запись другого мастера.");
        }

        if (apiException.StatusCode == HttpStatusCode.Conflict)
        {
            return Loc.T("Запись уже изменилась — обновите список.");
        }

        return apiException.StatusCode switch
        {
            HttpStatusCode.Unauthorized => SessionExpiredText,
            HttpStatusCode.TooManyRequests => TooManyRequestsText,
            _ => fallback,
        };
    }

    /// <summary>
    /// Причина неудачного создания записи клиента и признак «перечитать слоты».
    /// </summary>
    /// <remarks>
    /// <para>
    /// Коды важнее статуса: <c>booking.add.overlap</c> и «начало вне окна» лечатся только
    /// новым свободным временем, а <c>booking.create.customerNotFound</c> значит, что
    /// пользователь исчез или заблокирован, — это ошибка поиска, а не «страница не найдена».
    /// </para>
    /// <para>
    /// Тексты совпадают с <c>describeBarberBookingCreateFailure</c> веба. Английский
    /// <c>detail</c> не показывается никогда: сервер присылает его для логов.
    /// </para>
    /// </remarks>
    /// <param name="exception">Исключение запроса.</param>
    /// <param name="fallback">Запасной текст формы.</param>
    /// <returns>Текст для человека и необходимость перечитать сетку.</returns>
    public static BarberBookingCreateFailure CreateError(Exception? exception, string fallback)
    {
        if (exception is HttpRequestException or OperationCanceledException)
        {
            return new BarberBookingCreateFailure(NetworkErrorText, false);
        }

        if (exception is not BarberSearchApiException apiException)
        {
            return new BarberBookingCreateFailure(fallback, false);
        }

        if (apiException.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new BarberBookingCreateFailure(SessionExpiredText, false);
        }

        if (apiException.StatusCode == HttpStatusCode.Forbidden)
        {
            return new BarberBookingCreateFailure(RecordsForbiddenText, false);
        }

        if (HasCode(apiException, ApiErrorCodes.Booking.CreateCustomerNotFound))
        {
            return new BarberBookingCreateFailure(
                Loc.T("Пользователь не найден — возможно, аккаунт заблокирован. Повторите поиск."),
                false);
        }

        if (HasCode(apiException, CreateCustomerAmbiguousCode))
        {
            return new BarberBookingCreateFailure(
                Loc.T("Выберите зарегистрированного клиента или заполните данные нового — не оба варианта сразу."),
                false);
        }

        if (HasCode(apiException, CreateCustomerCode)
            || HasCode(apiException, CreateGuestFirstNameCode)
            || HasCode(apiException, CreateGuestPhoneCode))
        {
            return new BarberBookingCreateFailure(Loc.T("Укажите имя и телефон клиента."), false);
        }

        if (HasCode(apiException, ApiErrorCodes.Booking.AddOverlap))
        {
            return new BarberBookingCreateFailure(
                Loc.T("Это время только что заняли. Выберите другое."),
                true);
        }

        if (HasCode(apiException, CreateStartAtInPastCode)
            || HasCode(apiException, CreateStartAtTooFarCode))
        {
            return new BarberBookingCreateFailure(
                Loc.T("Это время уже недоступно. Обновите свободное время."),
                true);
        }

        if (apiException.StatusCode == HttpStatusCode.NotFound)
        {
            return new BarberBookingCreateFailure(
                Loc.T("Услуга или салон больше недоступны. Выберите другой вариант."),
                true);
        }

        if (HasCode(apiException, ApiErrorCodes.Booking.AddLockTimeout))
        {
            return new BarberBookingCreateFailure(
                Loc.T("Не удалось занять время, попробуйте ещё раз."),
                false);
        }

        return new BarberBookingCreateFailure(
            apiException.StatusCode == HttpStatusCode.TooManyRequests
                ? TooManyRequestsText
                : fallback,
            false);
    }

    /// <summary>
    /// Причина неудачного переноса визита и признак «перечитать сетку».
    /// </summary>
    /// <remarks>
    /// Коды важнее статуса: <c>overlap</c> и остаточная гонка <c>update.overlap</c> значат
    /// одно — время занято; <c>slot</c> — сетка устарела; <c>startAt.inPast</c> — начало
    /// уже прошло. Все три требуют новой сетки, а «слишком далеко» и чужой визит — нет.
    /// </remarks>
    /// <param name="exception">Исключение запроса.</param>
    /// <param name="fallback">Запасной текст экрана.</param>
    /// <returns>Текст для человека и необходимость перечитать сетку.</returns>
    public static BarberRescheduleFailure RescheduleError(Exception? exception, string fallback)
    {
        if (exception is HttpRequestException or OperationCanceledException)
        {
            return new BarberRescheduleFailure(NetworkErrorText, false);
        }

        if (exception is not BarberSearchApiException apiException)
        {
            return new BarberRescheduleFailure(fallback, false);
        }

        if (HasCode(apiException, ApiErrorCodes.Booking.RescheduleOverlap)
            || HasCode(apiException, UpdateOverlapCode))
        {
            return new BarberRescheduleFailure(
                Loc.T("Это время уже занято. Выберите другое."),
                true);
        }

        if (HasCode(apiException, ApiErrorCodes.Booking.RescheduleSlot))
        {
            return new BarberRescheduleFailure(
                Loc.T("Это время больше не доступно. Выберите другое."),
                true);
        }

        if (HasCode(apiException, ApiErrorCodes.Booking.RescheduleStartAtInPast))
        {
            return new BarberRescheduleFailure(
                Loc.T("Это время уже прошло. Выберите другое."),
                true);
        }

        if (HasCode(apiException, ApiErrorCodes.Booking.RescheduleStartAtTooFar))
        {
            return new BarberRescheduleFailure(Loc.T("Слишком далеко в будущем."), false);
        }

        if (HasCode(apiException, ApiErrorCodes.Booking.RescheduleStatus))
        {
            return new BarberRescheduleFailure(
                Loc.T("Запись уже нельзя перенести — обновите страницу."),
                false);
        }

        if (HasCode(apiException, ApiErrorCodes.Booking.RescheduleLockTimeout))
        {
            return new BarberRescheduleFailure(
                Loc.T("Не удалось перенести: попробуйте ещё раз."),
                true);
        }

        if (HasCode(apiException, ApiErrorCodes.Booking.UpdateConcurrencyConflict))
        {
            return new BarberRescheduleFailure(
                Loc.T("Запись изменилась — обновите страницу."),
                true);
        }

        if (apiException.StatusCode == HttpStatusCode.NotFound
            || HasCode(apiException, ApiErrorCodes.Booking.NotFound))
        {
            return new BarberRescheduleFailure(Loc.T("Запись не найдена."), false);
        }

        if (apiException.StatusCode == HttpStatusCode.Forbidden
            || HasCode(apiException, ApiErrorCodes.Auth.Forbidden))
        {
            return new BarberRescheduleFailure(Loc.T("Это запись другого мастера."), false);
        }

        return new BarberRescheduleFailure(
            apiException.StatusCode switch
            {
                HttpStatusCode.Unauthorized => SessionExpiredText,
                HttpStatusCode.TooManyRequests => TooManyRequestsText,
                _ => fallback,
            },
            false);
    }

    /// <summary>
    /// Форма счётчика окон: «1 окно», «2 окна», «5 окон».
    /// </summary>
    /// <remarks>
    /// Отдельного <c>PluralKey</c> для окон в каталоге Core нет, а менять общий файл
    /// ради одной подписи нельзя. Поэтому формы выбираются здесь по тем же правилам,
    /// что <c>frontend/src/i18n/plurals.ts</c>; английский вариант идёт через
    /// собственный шаблон, чтобы непереведённая русская форма не попала в <c>en</c>.
    /// </remarks>
    private static string GapsCount(int count)
    {
        if (Loc.Locale == Locale.En)
        {
            return Loc.T(count == 1 ? "{0} gap" : "{0} gaps", count);
        }

        var rest = Math.Abs(count) % 100;
        var last = rest % 10;
        var template = rest is >= 11 and <= 14
            ? "{0} окон"
            : last switch
            {
                1 => "{0} окно",
                2 or 3 or 4 => "{0} окна",
                _ => "{0} окон",
            };

        return Loc.T(template, count);
    }

    /// <summary>
    /// Проверяет наличие публичного кода ошибки.
    /// </summary>
    private static bool HasCode(BarberSearchApiException exception, string code) =>
        exception.ErrorCodes.Contains(code, StringComparer.Ordinal);
}

/// <summary>
/// Результат неудачного создания записи клиента мастером.
/// </summary>
/// <param name="Message">Текст для человека.</param>
/// <param name="ReloadSlots">
/// <c>true</c>, когда время заняли или салон с услугой исчезли: помочь может только
/// перечитанная сетка той же даты, а форма остаётся заполненной.
/// </param>
public sealed record BarberBookingCreateFailure(string Message, bool ReloadSlots);

/// <summary>
/// Результат неудачного переноса визита.
/// </summary>
/// <param name="Message">Текст для человека.</param>
/// <param name="ReloadSlots">
/// <c>true</c>, когда сетку нужно перечитать: время заняли, слот исчез или запись изменилась.
/// </param>
public sealed record BarberRescheduleFailure(string Message, bool ReloadSlots);
