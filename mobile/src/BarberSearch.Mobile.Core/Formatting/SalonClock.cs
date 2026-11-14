using System.Globalization;

namespace BarberSearch.Mobile.Core.Formatting;

/// <summary>
/// Время салона: перевод UTC-инстантов в пояс салона и календарные даты.
/// </summary>
/// <remarks>
/// <para>
/// Сервер присылает слоты инстантами в UTC и отдельно <c>timeZoneId</c> салона (IANA).
/// Показывать «18:30» нужно именно в поясе салона: часовой пояс телефона к записи отношения
/// не имеет — клиент может находиться в другом городе, а визит всё равно в салоне.
/// </para>
/// <para>
/// Неизвестный пояс не роняет экран: <see cref="TryResolve"/> возвращает <c>null</c>, и время
/// показывается в UTC. Это осознанное отклонение от веба (там подставляется пояс браузера):
/// у телефона нет «своего» пояса салона, из которого можно было бы взять замену.
/// </para>
/// <para>
/// Даты — календарные: <c>YYYY-MM-DD</c> из <c>DatePicker</c> и <c>DateOnly</c> из ответа
/// слотов. Переводить их в UTC нельзя, иначе восточнее UTC дата уехала бы на день назад.
/// </para>
/// </remarks>
public static class SalonClock
{
    /// <summary>
    /// Культура показа дат и цен: русская, задана явно и не зависит от настроек устройства.
    /// </summary>
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>
    /// Ищет пояс по IANA-идентификатору, не бросая исключений.
    /// </summary>
    /// <remarks>
    /// Исторические или чужие данные могут содержать неизвестный пояс; экран обязан
    /// показать хоть что-то, а не упасть на первом слоте.
    /// </remarks>
    /// <param name="timeZoneId">IANA-идентификатор из ответа API.</param>
    /// <returns>Пояс или <c>null</c>, если идентификатор пуст или не распознан.</returns>
    public static TimeZoneInfo? TryResolve(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Переводит инстант в пояс салона.
    /// </summary>
    /// <param name="instant">UTC-инстант слота.</param>
    /// <param name="timeZone">Пояс салона.</param>
    /// <returns>Тот же момент со сдвигом салона.</returns>
    public static DateTimeOffset ToLocal(DateTimeOffset instant, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return TimeZoneInfo.ConvertTime(instant, timeZone);
    }

    /// <summary>
    /// Время в поясе салона: «18:30».
    /// </summary>
    /// <param name="instant">UTC-инстант.</param>
    /// <param name="timeZone">Пояс салона; <c>null</c> — показываем UTC.</param>
    /// <returns>Время в круглосуточном формате.</returns>
    public static string FormatTime(DateTimeOffset instant, TimeZoneInfo? timeZone)
    {
        var local = timeZone is null ? instant.ToUniversalTime() : ToLocal(instant, timeZone);

        return local.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Интервал визита в поясе салона: «18:30–19:15».
    /// </summary>
    /// <param name="startAt">Начало в UTC.</param>
    /// <param name="endAt">Окончание в UTC.</param>
    /// <param name="timeZone">Пояс салона; <c>null</c> — UTC.</param>
    /// <returns>Время начала и конца через тире.</returns>
    public static string FormatTimeRange(
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        TimeZoneInfo? timeZone) =>
        string.Concat(FormatTime(startAt, timeZone), "–", FormatTime(endAt, timeZone));

    /// <summary>
    /// Календарная дата для показа: «воскресенье, 15 ноября».
    /// </summary>
    /// <remarks>
    /// Склонение месяца в русском зависит от числа, поэтому строка собирается форматтером
    /// культуры, а не списком названий.
    /// </remarks>
    /// <param name="date">Локальная дата салона.</param>
    /// <returns>День недели и число с месяцем.</returns>
    public static string FormatDate(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue).ToString("dddd, d MMMM", RuCulture);

    /// <summary>
    /// Сегодняшняя календарная дата в поясе салона.
    /// </summary>
    /// <remarks>
    /// Граница полуночи — главная причина, почему «сегодня» нельзя брать из пояса телефона:
    /// в Москве уже 15-е, а в UTC ещё 14-е, и сервер отверг бы дату из прошлого.
    /// </remarks>
    /// <param name="timeZone">Пояс салона; <c>null</c> — UTC.</param>
    /// <param name="now">Текущий момент; передаётся, чтобы поведение было проверяемым.</param>
    /// <returns>Дата в поясе салона.</returns>
    public static DateOnly Today(TimeZoneInfo? timeZone, DateTimeOffset now)
    {
        var local = timeZone is null ? now.ToUniversalTime() : ToLocal(now, timeZone);

        return DateOnly.FromDateTime(local.DateTime);
    }
}
