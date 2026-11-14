using System.Globalization;
using BarberSearch.Domain.Bookings;

namespace BarberSearch.Mobile.Core.Formatting;

/// <summary>
/// Тексты записи: статусы, даты, моменты визита, обратный отсчёт и заметки.
/// </summary>
/// <remarks>
/// <para>
/// Формулировки взяты из веба (<c>frontend/src/lib/booking-status.ts</c>,
/// <c>frontend/src/lib/format.ts</c>) и собраны здесь, чтобы страница не собирала строки сама:
/// одна копия текста на два клиента.
/// </para>
/// <para>
/// Культура задана явно (<c>ru-RU</c>): настройки телефона в другой локали дали бы «15 Nov»
/// и другую форму месяца. Обратный отсчёт повторяет правило веба: он существует только до
/// семи дней — на дальнем горизонте точность минуты ничего не значит.
/// </para>
/// </remarks>
public static class BookingText
{
    /// <summary>
    /// Подпись барбера, пока карточка не загрузилась.
    /// </summary>
    public const string BarberFallback = "Барбер";

    /// <summary>
    /// Подпись услуги, пока карточка не загрузилась.
    /// </summary>
    public const string ServiceFallback = "Услуга";

    /// <summary>
    /// Подпись салона, пока карточка не загрузилась.
    /// </summary>
    public const string SalonFallback = "Салон";

    /// <summary>
    /// Объявление после успешно отправленного отзыва: он уходит на модерацию.
    /// </summary>
    public const string ReviewSubmittedAnnouncement =
        "Спасибо! Отзыв отправлен на проверку: он появится на карточке барбера после модерации.";

    /// <summary>
    /// Максимальная длина комментария к отзыву — та же, что проверяет сервер.
    /// </summary>
    public const int ReviewTextMaxLength = 2000;

    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>
    /// Русские подписи статусов; значения API остаются английскими.
    /// </summary>
    private static readonly Dictionary<BookingStatus, string> Statuses = new()
    {
        [BookingStatus.Pending] = "Ждёт подтверждения",
        [BookingStatus.Confirmed] = "Подтверждена",
        [BookingStatus.Cancelled] = "Отменена",
        [BookingStatus.Completed] = "Завершена",
        [BookingStatus.NoShow] = "Неявка",
    };

    /// <summary>
    /// Подпись статуса записи.
    /// </summary>
    /// <param name="status">Статус из ответа API.</param>
    /// <returns>Русская подпись; неизвестный статус показывается как есть.</returns>
    public static string Status(BookingStatus status) =>
        Statuses.TryGetValue(status, out var text) ? text : status.ToString();

    /// <summary>
    /// Короткая дата строки списка: «сб, 15 нояб.».
    /// </summary>
    /// <param name="date">Локальная дата салона.</param>
    /// <returns>День недели, число и сокращённый месяц.</returns>
    public static string ShortDate(DateOnly date) => date.ToString("ddd, d MMM", RuCulture);

    /// <summary>
    /// Короткий день недели: «сб».
    /// </summary>
    /// <param name="date">Локальная дата салона.</param>
    /// <returns>Сокращённый день недели.</returns>
    public static string Weekday(DateOnly date) => date.ToString("ddd", RuCulture);

    /// <summary>
    /// Заголовок группы месяцев: «Ноябрь 2026».
    /// </summary>
    /// <remarks>
    /// В русской культуре месяц пишется со строчной буквы, поэтому первая буква поднимается
    /// здесь: заголовок группы — это заголовок, а не продолжение фразы.
    /// </remarks>
    /// <param name="date">Любая дата месяца.</param>
    /// <returns>Месяц с заголовочной буквой и год.</returns>
    public static string MonthTitle(DateOnly date)
    {
        var month = date.ToString("MMMM", RuCulture);
        var capitalized = char.ToUpperInvariant(month[0]) + month[1..];

        return string.Concat(capitalized, " ", date.Year.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Полный момент визита: «сб, 15 ноября в 18:30».
    /// </summary>
    /// <param name="instant">UTC-инстант.</param>
    /// <param name="timeZone">Пояс салона; <c>null</c> — UTC.</param>
    /// <returns>Дата и время в поясе салона.</returns>
    public static string Moment(DateTimeOffset instant, TimeZoneInfo? timeZone)
    {
        var local = timeZone is null ? instant.ToUniversalTime() : SalonClock.ToLocal(instant, timeZone);

        return string.Concat(
            local.ToString("ddd, d MMMM", RuCulture),
            " в ",
            local.ToString("HH:mm", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Обратный отсчёт до визита: «меньше минуты», «через 2 часа», «через 3 дня».
    /// </summary>
    /// <remarks>
    /// Правило веба: отсчёт существует только на горизонте семи дней и не показывается для
    /// прошедшего времени. Округление — до ближайшего значения, как в браузере: «через 1 час»
    /// на 59-й минуте полезнее, чем «через 59 минут».
    /// </remarks>
    /// <param name="startAt">Начало визита.</param>
    /// <param name="now">Текущий момент.</param>
    /// <returns>Строка отсчёта либо <c>null</c>, если показывать нечего.</returns>
    public static string? Countdown(DateTimeOffset startAt, DateTimeOffset now)
    {
        var difference = startAt - now;

        if (difference <= TimeSpan.Zero)
        {
            return null;
        }

        var minutes = (int)Math.Round(difference.TotalMinutes, MidpointRounding.AwayFromZero);

        if (minutes < 1)
        {
            return "меньше минуты";
        }

        if (minutes < 60)
        {
            return string.Concat("через ", Plural(minutes, "минуту", "минуты", "минут"));
        }

        var hours = (int)Math.Round(minutes / 60.0, MidpointRounding.AwayFromZero);

        if (hours < 24)
        {
            return string.Concat("через ", Plural(hours, "час", "часа", "часов"));
        }

        var days = (int)Math.Round(hours / 24.0, MidpointRounding.AwayFromZero);

        return days <= 7
            ? string.Concat("через ", Plural(days, "день", "дня", "дней"))
            : null;
    }

    /// <summary>
    /// Заметка барбера строкой карточки: «Барбер: Покороче сзади».
    /// </summary>
    /// <param name="note">Заметка из ответа API.</param>
    /// <returns>Подписанная строка либо <c>null</c>, если заметки нет.</returns>
    public static string? BarberNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : string.Concat("Барбер: ", note.Trim());

    /// <summary>
    /// Заметка клиента строкой карточки: «Вы: Покороче сзади».
    /// </summary>
    /// <param name="note">Заметка из ответа API.</param>
    /// <returns>Подписанная строка либо <c>null</c>, если заметки нет.</returns>
    public static string? CustomerNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : string.Concat("Вы: ", note.Trim());

    /// <summary>
    /// Причина отмены для истории: с причиной или «Причина отмены не указана».
    /// </summary>
    /// <remarks>
    /// Причину оставляет только барбер: клиент отменяет без неё, и тогда строка честно
    /// говорит, что причины нет, а не прячет сам факт отмены.
    /// </remarks>
    /// <param name="reason">Причина из ответа API.</param>
    /// <returns>Строка для карточки.</returns>
    public static string CancellationReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? "Причина отмены не указана"
            : string.Concat("Причина отмены: ", reason.Trim());

    /// <summary>
    /// Объявление об отмене: «Запись на {when} отменена.».
    /// </summary>
    /// <param name="when">Момент визита в поясе салона.</param>
    /// <returns>Текст баннера.</returns>
    public static string CancelAnnouncement(string when) =>
        string.Concat("Запись на ", when, " отменена.");

    /// <summary>
    /// Объявление о переносе: «Визит перенесён на {when}.».
    /// </summary>
    /// <param name="when">Новый момент визита в поясе салона.</param>
    /// <returns>Текст баннера.</returns>
    public static string RescheduleAnnouncement(string when) =>
        string.Concat("Визит перенесён на ", when, ".");

    /// <summary>
    /// Склоняет существительное после числа: «1 минуту», «2 минуты», «5 минут».
    /// </summary>
    /// <param name="count">Число (неотрицательное).</param>
    /// <param name="one">Форма для 1, 21, 31…</param>
    /// <param name="few">Форма для 2–4, 22–24…</param>
    /// <param name="many">Форма для 0, 5–20, 25–30…</param>
    /// <returns>Число и правильная форма слова.</returns>
    private static string Plural(int count, string one, string few, string many)
    {
        var rest = Math.Abs(count) % 100;
        var last = rest % 10;

        var form = rest is >= 11 and <= 14
            ? many
            : last switch
            {
                1 => one,
                2 or 3 or 4 => few,
                _ => many,
            };

        return string.Concat(count.ToString(CultureInfo.InvariantCulture), " ", form);
    }
}
