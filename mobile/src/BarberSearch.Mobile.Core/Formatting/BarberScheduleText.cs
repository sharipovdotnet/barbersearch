using System.Globalization;
using System.Net;
using BarberSearch.Domain.Bookings;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Localization;

namespace BarberSearch.Mobile.Core.Formatting;

/// <summary>
/// Результат неудачной записи расписания: текст для человека и признак конфликта.
/// </summary>
/// <remarks>
/// <para>
/// Конфликт — не ошибка: сервер отказался сохранять правку только потому, что активные визиты
/// выйдут за новые часы. Экрану нужно не сообщение, а диалог подтверждения, поэтому одной
/// строки мало: вместе с текстом возвращается <see cref="IsConflict"/>, по которому экран
/// решает, показать причину или спросить «Всё равно сохранить?».
/// </para>
/// <para>
/// Пустой <see cref="Message"/> не бывает: если конфликтов в ответе нет, текст говорит, что
/// записи изменились, — молчаливая неудача хуже неточной формулировки.
/// </para>
/// </remarks>
/// <param name="Message">Текст причины для человека.</param>
/// <param name="IsConflict">
/// Сервер ответил <c>409</c>: правку можно повторить с <c>force</c>.
/// </param>
public sealed record ScheduleWriteFailure(string Message, bool IsConflict);

/// <summary>
/// Тексты раздела «Расписание»: дни недели, даты, интервалы и ошибки записи.
/// </summary>
/// <remarks>
/// <para>
/// Формулировки повторяют веб (<c>frontend/src/lib/barber-schedule.ts</c>,
/// <c>barber-week-schedule.tsx</c>, <c>barber-schedule-exceptions.tsx</c>,
/// <c>barber-time-offs.tsx</c>), но собраны в Core: страница не форматирует строки сама.
/// </para>
/// <para>
/// Время недели и исключений — локальное время салона, его нельзя пересчитывать в пояс
/// телефона. Поэтому <see cref="TimeRange"/> печатает <see cref="TimeOnly"/> как есть, а
/// <see cref="TimeOffRange"/> переводит UTC-инстанты отпуска в пояс салона.
/// </para>
/// <para>
/// Сообщения сервера (<c>detail</c>) человеку не показываются: они английские и написаны для
/// разработчика. Экран выбирает русскую строку по публичному коду ошибки, а незнакомый ответ
/// сводит к подписи раздела.
/// </para>
/// </remarks>
public static class BarberScheduleText
{
    /// <summary>
    /// Максимальная длина пометки исключения или отпуска — та же, что проверяет сервер.
    /// </summary>
    public const int ReasonMaxLength = 500;

    /// <summary>
    /// Русские подписи дней недели; порядок задаётся вызывающим кодом, а не словарём.
    /// </summary>
    private static readonly Dictionary<DayOfWeek, string> Weekdays = new()
    {
        [DayOfWeek.Monday] = "Понедельник",
        [DayOfWeek.Tuesday] = "Вторник",
        [DayOfWeek.Wednesday] = "Среда",
        [DayOfWeek.Thursday] = "Четверг",
        [DayOfWeek.Friday] = "Пятница",
        [DayOfWeek.Saturday] = "Суббота",
        [DayOfWeek.Sunday] = "Воскресенье",
    };

    /// <summary>
    /// Причина транспортного сбоя — одна на чтение и запись.
    /// </summary>
    private static string NetworkValue =>
        Loc.T("Нет связи с сервером. Проверьте подключение и повторите попытку.");

    /// <summary>
    /// Ответ <c>429</c>: сервер просит подождать.
    /// </summary>
    private static string RateLimitValue =>
        Loc.T("Слишком много запросов. Подождите минуту и повторите.");

    /// <summary>
    /// Ответ <c>401</c>: токен истёк, нужен повторный вход.
    /// </summary>
    private static string SessionExpiredValue =>
        Loc.T("Сессия истекла — войдите снова.");

    /// <summary>
    /// Ответ <c>403</c>: расписание чужого профиля.
    /// </summary>
    private static string ForbiddenValue =>
        Loc.T("Расписание доступно только владельцу профиля.");

    /// <summary>
    /// Ответ <c>404</c>: профиль или салон исчезли.
    /// </summary>
    private static string NotFoundValue =>
        Loc.T("Профиль или салон не найдены. Обновите страницу.");

    /// <summary>
    /// <c>409</c> без разобранных конфликтов: записи успели измениться.
    /// </summary>
    /// <remarks>
    /// Единственный случай, когда конфликт есть, а показать его нечем: сервер отказал, но
    /// расширение <c>conflicts</c> пустое или нечитаемое. Повторять вслепую нельзя — человек
    /// должен перечитать расписание.
    /// </remarks>
    private static string StaleConflictsValue =>
        Loc.T("Расписание не сохранено: записи изменились, обновите страницу.");

    /// <summary>
    /// Подпись дня недели: «Понедельник»… «Воскресенье».
    /// </summary>
    /// <param name="dayOfWeek">День недели из ответа API.</param>
    /// <returns>Русская подпись; неизвестное значение показывается как есть.</returns>
    public static string Weekday(DayOfWeek dayOfWeek) =>
        Loc.T(Weekdays.TryGetValue(dayOfWeek, out var text) ? text : dayOfWeek.ToString());

    /// <summary>
    /// Календарная дата: «15 ноября 2026».
    /// </summary>
    /// <remarks>
    /// Год обязателен: исключения и отпуска живут на горизонте месяцев, и без года «15 ноября»
    /// ничего не говорит. «г.» не добавляется — это часть стандартного длинного формата, а не
    /// культура.
    /// </remarks>
    /// <param name="date">Локальная дата салона.</param>
    /// <returns>День, полный месяц и год.</returns>
    public static string Date(DateOnly date) => date.ToString("d MMMM yyyy", Loc.Culture);

    /// <summary>
    /// Часы работы: «10:00–20:00».
    /// </summary>
    /// <param name="start">Начало рабочего дня.</param>
    /// <param name="end">Окончание рабочего дня.</param>
    /// <returns>Время через среднее тире, как в макете.</returns>
    public static string TimeRange(TimeOnly start, TimeOnly end) => string.Concat(
        start.ToString("HH:mm", CultureInfo.InvariantCulture),
        "–",
        end.ToString("HH:mm", CultureInfo.InvariantCulture));

    /// <summary>
    /// Период отпуска: «15–17 ноября 2026» или «15 ноября — 2 декабря 2026».
    /// </summary>
    /// <remarks>
    /// <para>
    /// Верхняя граница периода исключающая: сервер хранит отпуск как «с 00:00 первого дня по
    /// 00:00 дня после последнего». Поэтому последний день — это <paramref name="end"/> минус
    /// секунда, а не сам <paramref name="end"/>: иначе недельный отпуск показался бы
    /// восьмидневным.
    /// </para>
    /// <para>
    /// Один месяц не повторяет месяц дважды, разные месяцы одного года не повторяют год, а
    /// разные годы печатаются целиком — так строка остаётся короткой и однозначной.
    /// </para>
    /// </remarks>
    /// <param name="start">Начало периода в UTC.</param>
    /// <param name="end">Окончание периода в UTC (исключающее).</param>
    /// <param name="timeZone">Пояс салона; <c>null</c> — UTC.</param>
    /// <returns>Дата или диапазон дат.</returns>
    public static string TimeOffRange(
        DateTimeOffset start,
        DateTimeOffset end,
        TimeZoneInfo? timeZone)
    {
        var startLocal = ToLocal(start, timeZone);
        var endLocal = ToLocal(end, timeZone);
        var startDate = DateOnly.FromDateTime(startLocal.DateTime);
        var endDate = DateOnly.FromDateTime((endLocal - TimeSpan.FromSeconds(1)).DateTime);

        if (endDate < startDate)
        {
            endDate = startDate;
        }

        if (startDate == endDate)
        {
            return Date(startDate);
        }

        if (startDate.Year == endDate.Year && startDate.Month == endDate.Month)
        {
            return string.Concat(
                startDate.Day.ToString(CultureInfo.InvariantCulture),
                "–",
                endDate.Day.ToString(CultureInfo.InvariantCulture),
                " ",
                MonthAndYear(endDate));
        }

        if (startDate.Year == endDate.Year)
        {
            return string.Concat(
                startDate.ToString("d MMMM", Loc.Culture),
                " — ",
                endDate.ToString("d MMMM yyyy", Loc.Culture));
        }

        return string.Concat(Date(startDate), " — ", Date(endDate));
    }

    /// <summary>
    /// Момент визита: «сб, 15 ноября в 10:00».
    /// </summary>
    /// <param name="start">Начало визита в UTC.</param>
    /// <param name="timeZone">Пояс салона; <c>null</c> — UTC.</param>
    /// <returns>Короткий день недели, дата и время.</returns>
    public static string ConflictWhen(DateTimeOffset start, TimeZoneInfo? timeZone) =>
        BookingText.Moment(start, timeZone);

    /// <summary>
    /// Подпись статуса записи: «Ждёт подтверждения», «Подтверждена».
    /// </summary>
    /// <param name="status">Статус из ответа API.</param>
    /// <returns>Русская подпись.</returns>
    public static string Status(BookingStatus status) => BookingText.Status(status);

    /// <summary>
    /// Локальная проверка часов: конец рабочего дня не позже начала.
    /// </summary>
    /// <remarks>
    /// Та же строка, что приходит с кодом <c>barberSalon.*.timeRange</c>: человек не должен
    /// видеть две разные формулировки одной ошибки — до сохранения и после ответа сервера.
    /// </remarks>
    public static string InvalidWorkingHours =>
        Loc.T("Конец рабочего дня должен быть позже начала.");

    /// <summary>
    /// Локальная проверка пометки: длиннее 500 символов сервер отклонит.
    /// </summary>
    public static string ReasonTooLong =>
        Loc.T("Пометка слишком длинная — максимум 500 символов.");

    /// <summary>
    /// Локальная проверка периода отпуска: конец раньше начала.
    /// </summary>
    /// <remarks>
    /// Та же строка, что у кода <c>barberProfile.addTimeOff.period</c>: локальная проверка
    /// отсекает заведомо неверный ввод, а сервер повторяет правило для чужих клиентов.
    /// </remarks>
    public static string InvalidTimeOffPeriod =>
        Loc.T("Конец периода должен быть позже начала.");

    /// <summary>
    /// Текст ошибки чтения расписания.
    /// </summary>
    /// <remarks>
    /// Отмена сюда не попадает: экран гасит её сам, до выбора текста. Серверный <c>detail</c>
    /// не показывается — он английский.
    /// </remarks>
    /// <param name="exception">Исключение чтения.</param>
    /// <param name="fallback">Подпись раздела, если ответ незнаком.</param>
    /// <returns>Причина для человека.</returns>
    public static string ReadError(Exception exception, string fallback)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(fallback);

        return exception switch
        {
            HttpRequestException or OperationCanceledException => NetworkValue,
            BarberSearchApiException apiException => ReadApiError(apiException, fallback),
            _ => fallback,
        };
    }

    /// <summary>
    /// Текст ошибки записи расписания и признак конфликта.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Порядок важен: публичный код ошибки точнее статуса. Например <c>lockTimeout</c> тоже
    /// может прийти с <c>409</c>, но это повторяемая блокировка, а не конфликт визитов —
    /// показывать диалог «всё равно сохранить» там нечего.
    /// </para>
    /// <para>
    /// Известный статус (<c>401</c>, <c>403</c>, <c>404</c>, <c>429</c>) переводится и без кода,
    /// потому что прокси и старые версии сервера кодов не присылают. Всё остальное сводится к
    /// подписи раздела; <paramref name="operation"/> нужен, только когда подписи нет вовсе.
    /// </para>
    /// </remarks>
    /// <param name="exception">Исключение записи.</param>
    /// <param name="fallback">Подпись раздела, если ответ незнаком.</param>
    /// <param name="operation">Действие в инфинитиве, ключ каталога: «сохранить расписание».</param>
    /// <returns>Причина и признак конфликта.</returns>
    public static ScheduleWriteFailure WriteError(
        Exception exception,
        string fallback,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(operation);

        if (exception is BarberSearchApiException apiException)
        {
            var code = apiException.ErrorCodes
                .Where(KnownCodeMessages.ContainsKey)
                .Select(code => KnownCodeMessages[code])
                .FirstOrDefault();

            if (code is not null)
            {
                return new ScheduleWriteFailure(Loc.T(code), IsConflict: false);
            }

            if (apiException.StatusCode == HttpStatusCode.Conflict)
            {
                return new ScheduleWriteFailure(StaleConflictsValue, IsConflict: true);
            }

            return new ScheduleWriteFailure(ReadApiError(apiException, fallback), IsConflict: false);
        }

        if (exception is HttpRequestException or OperationCanceledException)
        {
            return new ScheduleWriteFailure(NetworkValue, IsConflict: false);
        }

        var message = string.IsNullOrWhiteSpace(fallback)
            ? Loc.T("Не удалось {0}.", Loc.T(operation))
            : fallback;

        return new ScheduleWriteFailure(message, IsConflict: false);
    }

    /// <summary>
    /// Русские тексты публичных кодов записи расписания.
    /// </summary>
    /// <remarks>
    /// Ключ — код из <c>errorCodes</c>, а не <c>detail</c>: коды стабильны и переведены в вебе
    /// (<c>describeScheduleError</c>), тогда как текст сервера меняется и английский.
    /// </remarks>
    private static readonly Dictionary<string, string> KnownCodeMessages = new(StringComparer.Ordinal)
    {
        ["barberSalon.replaceSchedule.days"] = "Нужно заполнить все семь дней недели.",
        ["barberSalon.replaceSchedule.duplicateDay"] =
            "День недели указан дважды. Обновите страницу и повторите.",
        ["barberSalon.replaceSchedule.timeRange"] =
            "Конец рабочего дня должен быть позже начала.",
        ["barberSalon.setScheduleException.timeRange"] =
            "Конец рабочего дня должен быть позже начала.",
        ["barberSalon.setScheduleException.reasonTooLong"] =
            "Пометка слишком длинная — максимум 500 символов.",
        ["barberSalon.removeScheduleException.notFound"] = "Это исключение уже удалено.",
        ["barberProfile.addTimeOff.overlap"] = "Этот период пересекается с другим отпуском.",
        ["barberProfile.addTimeOff.period"] = "Конец периода должен быть позже начала.",
        ["barberSchedule.lockTimeout"] = "Расписание не сохранено: попробуйте ещё раз.",
    };

    /// <summary>
    /// Текст сбоя API по статусу ответа.
    /// </summary>
    /// <param name="exception">Ответ сервера с ошибкой.</param>
    /// <param name="fallback">Подпись раздела.</param>
    /// <returns>Причина для человека.</returns>
    private static string ReadApiError(BarberSearchApiException exception, string fallback) =>
        exception.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => RateLimitValue,
            HttpStatusCode.Unauthorized => SessionExpiredValue,
            HttpStatusCode.Forbidden => ForbiddenValue,
            HttpStatusCode.NotFound => NotFoundValue,
            _ => fallback,
        };

    /// <summary>
    /// Переводит инстант в пояс салона; неизвестный пояс — UTC.
    /// </summary>
    /// <param name="instant">UTC-инстант.</param>
    /// <param name="timeZone">Пояс салона; <c>null</c> — UTC.</param>
    /// <returns>Тот же момент в поясе салона.</returns>
    private static DateTimeOffset ToLocal(DateTimeOffset instant, TimeZoneInfo? timeZone) =>
        timeZone is null ? instant.ToUniversalTime() : SalonClock.ToLocal(instant, timeZone);

    /// <summary>
    /// Месяц и год в форме диапазона: «ноября 2026».
    /// </summary>
    /// <remarks>
    /// Родительный падеж появляется в русском только в шаблоне с числом: «MMMM yyyy» дал бы
    /// «ноябрь 2026», и «15–17 ноябрь 2026» читается как ошибка. Поэтому месяц берётся из
    /// формата с днём, а само число из строки убирается. Для английского это ничего не меняет:
    /// «17 November 2026» превращается в «November 2026».
    /// </remarks>
    /// <param name="date">Дата, чей месяц и год нужны.</param>
    /// <returns>Месяц и год без числа.</returns>
    private static string MonthAndYear(DateOnly date)
    {
        var text = date.ToString("d MMMM yyyy", Loc.Culture);
        var day = date.Day.ToString(CultureInfo.InvariantCulture);
        var index = text.IndexOf(day, StringComparison.Ordinal);

        return index < 0
            ? date.ToString("MMMM yyyy", Loc.Culture)
            : text.Remove(index, day.Length).Trim();
    }
}
