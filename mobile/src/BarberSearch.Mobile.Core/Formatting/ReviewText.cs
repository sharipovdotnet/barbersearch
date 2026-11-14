using System.Globalization;
using BarberSearch.Contracts.Reviews.GetMyReviews;

namespace BarberSearch.Mobile.Core.Formatting;

/// <summary>
/// Тексты отзывов клиента: статус, звёзды, дата отзыва, строка визита и тело.
/// </summary>
/// <remarks>
/// <para>
/// Всё взято из веба (<c>frontend/src/components/account/my-review-row.tsx</c>,
/// <c>review-status-badge.tsx</c>, <c>lib/format.ts</c>) и собрано здесь, чтобы страница
/// не склеивала строки руками: одна копия текста на два клиента.
/// </para>
/// <para>
/// Статусов ровно два. <c>isPublished = false</c> сервер отдаёт и для нового отзыва, и для
/// скрытого администратором — различить их нельзя, поэтому «На модерации» и есть честный
/// ответ на оба случая.
/// </para>
/// <para>
/// Время визита и дата отзыва считаются в поясе салона: визит был там, и часовой пояс
/// телефона к нему отношения не имеет. Неизвестный пояс показывает UTC — как
/// <see cref="SalonClock.TryResolve"/> и остальные экраны.
/// </para>
/// </remarks>
public static class ReviewText
{
    /// <summary>
    /// Подпись опубликованного отзыва.
    /// </summary>
    public const string Published = "Опубликован";

    /// <summary>
    /// Подпись отзыва, ожидающего проверки.
    /// </summary>
    public const string OnModeration = "На модерации";

    /// <summary>
    /// Тело отзыва, когда комментария нет.
    /// </summary>
    public const string NoComment = "Без комментария.";

    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>
    /// Подпись статуса публикации.
    /// </summary>
    /// <param name="isPublished">Статус из ответа API.</param>
    /// <returns>«Опубликован» либо «На модерации».</returns>
    public static string Status(bool isPublished) => isPublished ? Published : OnModeration;

    /// <summary>
    /// Звёзды одной строкой: «★★★★☆».
    /// </summary>
    /// <remarks>
    /// Иконок в проекте нет, поэтому рейтинг рисуется текстом — так же, как на экране отзыва.
    /// Оценка вне диапазона 1..5 обрезается: пустых звёзд всегда пять.
    /// </remarks>
    /// <param name="rating">Оценка от 1 до 5.</param>
    /// <returns>Пять символов: заполненные и пустые.</returns>
    public static string Stars(int rating)
    {
        var filled = Math.Clamp(rating, 0, 5);

        return string.Concat(
            new string('★', filled),
            new string('☆', 5 - filled));
    }

    /// <summary>
    /// Оценка числом: «5 / 5».
    /// </summary>
    /// <param name="rating">Оценка от 1 до 5.</param>
    /// <returns>Оценка и максимум через косую черту.</returns>
    public static string Rating(int rating) => string.Concat(
        rating.ToString(CultureInfo.InvariantCulture),
        " / 5");

    /// <summary>
    /// Дата отзыва в поясе салона: «15 ноября 2026».
    /// </summary>
    /// <param name="createdAt">Момент создания отзыва в UTC.</param>
    /// <param name="timeZone">Пояс салона; <c>null</c> — UTC.</param>
    /// <returns>Дата без времени.</returns>
    public static string ReviewDate(DateTimeOffset createdAt, TimeZoneInfo? timeZone) =>
        ToLocal(createdAt, timeZone).ToString("d MMMM yyyy", RuCulture);

    /// <summary>
    /// Момент визита в поясе салона: «15 ноября 2026 в 18:30».
    /// </summary>
    /// <remarks>
    /// Не <see cref="BookingText.Moment"/>: в списке отзывов день недели не нужен, а формат
    /// повторяет <c>formatDateTime</c> веба, где день недели тоже не выводится.
    /// </remarks>
    /// <param name="startAt">Начало визита в UTC.</param>
    /// <param name="timeZone">Пояс салона; <c>null</c> — UTC.</param>
    /// <returns>Дата и время визита.</returns>
    public static string VisitMoment(DateTimeOffset startAt, TimeZoneInfo? timeZone)
    {
        var local = ToLocal(startAt, timeZone);

        return string.Concat(
            local.ToString("d MMMM yyyy", RuCulture),
            " в ",
            local.ToString("HH:mm", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Строка визита: «15 ноября 2026 в 18:30 · Услуга · Салон · 2 400 ₽».
    /// </summary>
    /// <param name="visit">Визит из ответа API.</param>
    /// <param name="timeZone">Пояс салона; <c>null</c> — UTC.</param>
    /// <returns>Момент, услуга, салон и цена через разделитель.</returns>
    public static string VisitLine(MyReviewVisitResponse visit, TimeZoneInfo? timeZone)
    {
        ArgumentNullException.ThrowIfNull(visit);

        return string.Join(
            " · ",
            new[]
            {
                VisitMoment(visit.StartAt, timeZone),
                visit.ServiceName,
                visit.SalonName,
                BarberText.Price(visit.Price),
            }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>
    /// Тело отзыва: текст либо «Без комментария.».
    /// </summary>
    /// <param name="text">Комментарий из ответа API.</param>
    /// <returns>Текст отзыва без изменений либо подпись пустого.</returns>
    public static string Body(string? text) =>
        string.IsNullOrWhiteSpace(text) ? NoComment : text.Trim();

    /// <summary>
    /// Переводит инстант в пояс салона или в UTC, если пояс неизвестен.
    /// </summary>
    private static DateTimeOffset ToLocal(DateTimeOffset instant, TimeZoneInfo? timeZone) =>
        timeZone is null ? instant.ToUniversalTime() : SalonClock.ToLocal(instant, timeZone);
}
