using System.Globalization;

namespace BarberSearch.Application.Common;

/// <summary>
/// Преобразование времени между UTC и часовым поясом салона.
/// </summary>
/// <remarks>
/// <para>
/// Пояс салона хранится строкой IANA (<c>Salon.TimeZoneId</c>). На Windows системный
/// идентификатор может прийти и в старом виде (<c>Russian Standard Time</c>), поэтому
/// разбор сначала пробует строку как есть, а затем конвертирует Windows-идентификатор
/// в IANA. Неразрешимый пояс — <c>false</c>, а не исключение: тик фонового воркера
/// не должен падать из-за одной строки в данных (§4 спецификации варианта C).
/// </para>
/// <para>
/// Перевод локального времени в UTC возвращает <c>null</c> для несуществующего времени
/// (дыра перехода на летнее время): вызывающий пропускает такой момент, а не сдвигает его.
/// </para>
/// </remarks>
public static class SalonTimeZone
{
    /// <summary>
    /// Формат времени суток в ответах и текстах: часы и минуты без секунд.
    /// </summary>
    public const string TIME_OF_DAY_FORMAT = "HH:mm";

    /// <summary>
    /// Разрешает часовой пояс салона: сначала как системный идентификатор (IANA на Linux,
    /// IANA/Windows на Windows), затем — исторический Windows-идентификатор через
    /// конвертацию в IANA.
    /// </summary>
    /// <param name="timeZoneId">Идентификатор пояса из <c>Salon.TimeZoneId</c> или снимка уведомления.</param>
    /// <param name="timeZone">Найденный пояс; при <c>false</c> — <c>null</c>.</param>
    /// <returns><c>true</c>, если пояс разрешён.</returns>
    public static bool TryResolve(string? timeZoneId, out TimeZoneInfo timeZone)
    {
        timeZone = null!;

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        var candidate = timeZoneId.Trim();

        if (TimeZoneInfo.TryFindSystemTimeZoneById(candidate, out var found))
        {
            timeZone = found;
            return true;
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(candidate, out var ianaId)
            && TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out found))
        {
            timeZone = found;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Переводит локальное время салона в UTC. Несуществующее время (дыра перехода
    /// на летнее время) возвращает <c>null</c>.
    /// </summary>
    /// <param name="local">Локальное время без указания пояса.</param>
    /// <param name="timeZone">Пояс салона.</param>
    public static DateTimeOffset? ToUtc(DateTime local, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(unspecified))
        {
            return null;
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, timeZone);

        return new DateTimeOffset(utc);
    }

    /// <summary>
    /// Показывает момент в поясе салона как <c>HH:mm</c>.
    /// </summary>
    /// <param name="instant">Момент в UTC.</param>
    /// <param name="timeZone">Пояс салона.</param>
    public static string FormatTimeOfDay(DateTimeOffset instant, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var local = TimeZoneInfo.ConvertTime(instant, timeZone);

        return local.ToString(TIME_OF_DAY_FORMAT, CultureInfo.InvariantCulture);
    }
}
