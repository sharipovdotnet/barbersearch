using System.Globalization;
using BarberSearch.Domain.Bookings;
using BarberSearch.Mobile.Core.Localization;

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
/// Культура берётся из выбранного языка (<c>ru-RU</c> или <c>en-GB</c>), а не из настроек
/// телефона: иначе локаль устройства дала бы «15 Nov» и другую форму месяца. Английский
/// вариант — тот же <c>en-GB</c>, что в вебе. Обратный отсчёт повторяет правило веба: он
/// существует только до семи дней — на дальнем горизонте точность минуты ничего не значит.
/// </para>
/// </remarks>
public static class BookingText
{
    /// <summary>
    /// Подпись мастера, пока карточка не загрузилась.
    /// </summary>
    public static string BarberFallback => Loc.T("Мастер");

    /// <summary>
    /// Подпись услуги, пока карточка не загрузилась.
    /// </summary>
    public static string ServiceFallback => Loc.T("Услуга");

    /// <summary>
    /// Подпись салона, пока карточка не загрузилась.
    /// </summary>
    public static string SalonFallback => Loc.T("Салон");

    /// <summary>
    /// Объявление после успешно отправленного отзыва: он уходит на модерацию.
    /// </summary>
    public static string ReviewSubmittedAnnouncement => Loc.T(
        "Спасибо! Отзыв отправлен на проверку: он появится на карточке мастера после модерации.");

    /// <summary>
    /// Максимальная длина комментария к отзыву — та же, что проверяет сервер.
    /// </summary>
    public const int ReviewTextMaxLength = 2000;

    /// <summary>
    /// Подписи статусов; значения API остаются английскими.
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
        Loc.T(Statuses.TryGetValue(status, out var text) ? text : status.ToString());

    /// <summary>
    /// Короткая дата строки списка: «сб, 15 нояб.».
    /// </summary>
    /// <param name="date">Локальная дата салона.</param>
    /// <returns>День недели, число и сокращённый месяц.</returns>
    public static string ShortDate(DateOnly date) => date.ToString("ddd, d MMM", Loc.Culture);

    /// <summary>
    /// Короткий день недели: «сб».
    /// </summary>
    /// <param name="date">Локальная дата салона.</param>
    /// <returns>Сокращённый день недели.</returns>
    public static string Weekday(DateOnly date) => date.ToString("ddd", Loc.Culture);

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
        var month = date.ToString("MMMM", Loc.Culture);
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

        return Loc.T(
            "{0} в {1}",
            local.ToString("ddd, d MMMM", Loc.Culture),
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
            return Loc.T("меньше минуты");
        }

        if (minutes < 60)
        {
            return Loc.T("через {0}", Loc.Plural(PluralKey.Minutes, minutes));
        }

        var hours = (int)Math.Round(minutes / 60.0, MidpointRounding.AwayFromZero);

        if (hours < 24)
        {
            return Loc.T("через {0}", Loc.Plural(PluralKey.Hours, hours));
        }

        var days = (int)Math.Round(hours / 24.0, MidpointRounding.AwayFromZero);

        return days <= 7
            ? Loc.T("через {0}", Loc.Plural(PluralKey.Days, days))
            : null;
    }

    /// <summary>
    /// Заметка мастера строкой карточки: «Мастер: Покороче сзади».
    /// </summary>
    /// <param name="note">Заметка из ответа API.</param>
    /// <returns>Подписанная строка либо <c>null</c>, если заметки нет.</returns>
    public static string? BarberNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : Loc.T("Мастер: {0}", note.Trim());

    /// <summary>
    /// Заметка клиента строкой карточки: «Вы: Покороче сзади».
    /// </summary>
    /// <param name="note">Заметка из ответа API.</param>
    /// <returns>Подписанная строка либо <c>null</c>, если заметки нет.</returns>
    public static string? CustomerNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : Loc.T("Вы: {0}", note.Trim());

    /// <summary>
    /// Причина отмены для истории: с причиной или «Причина отмены не указана».
    /// </summary>
    /// <remarks>
    /// Причину оставляет только мастер: клиент отменяет без неё, и тогда строка честно
    /// говорит, что причины нет, а не прячет сам факт отмены.
    /// </remarks>
    /// <param name="reason">Причина из ответа API.</param>
    /// <returns>Строка для карточки.</returns>
    public static string CancellationReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? Loc.T("Причина отмены не указана")
            : Loc.T("Причина отмены: {0}", reason.Trim());

    /// <summary>
    /// Объявление об отмене: «Запись на {when} отменена.».
    /// </summary>
    /// <param name="when">Момент визита в поясе салона.</param>
    /// <returns>Текст баннера.</returns>
    public static string CancelAnnouncement(string when) =>
        Loc.T("Запись на {0} отменена.", when);

    /// <summary>
    /// Объявление о переносе: «Визит перенесён на {when}.».
    /// </summary>
    /// <param name="when">Новый момент визита в поясе салона.</param>
    /// <returns>Текст баннера.</returns>
    public static string RescheduleAnnouncement(string when) =>
        Loc.T("Визит перенесён на {0}.", when);

}
