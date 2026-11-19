using System.Globalization;
using BarberSearch.Mobile.Core.Localization;

namespace BarberSearch.Mobile.Core.Formatting;

/// <summary>
/// Тексты администрирования: ожидание в очереди, счётчики и бейджи.
/// </summary>
/// <remarks>
/// <para>
/// Числа форматируются по веб-правилам: бейдж рейла обрезается на «99+»
/// (<c>formatAdminBadgeCount</c>), а ожидание отзыва считается от попадания в очередь
/// и произносится словами — «ждёт 3 дня», а не датой (<c>formatWaitingSince</c>).
/// Дата визита здесь неуместна: у отзыва нет салона, в поясе которого её читать.
/// </para>
/// <para>
/// Порог «99» один на весь раздел: цифра в кружке не должна растить кружок.
/// </para>
/// </remarks>
public static class AdminText
{
    /// <summary>
    /// Наибольшее число, которое бейдж показывает цифрами; дальше — «+».
    /// </summary>
    public const int BadgeCap = 99;

    /// <summary>
    /// Число для бейджа: до <see cref="BadgeCap"/> включительно цифрами, дальше «99+».
    /// </summary>
    /// <param name="count">Число ожидающих.</param>
    /// <returns>Подпись бейджа.</returns>
    public static string BadgeCount(int count) => count > BadgeCap
        ? string.Concat(
            BadgeCap.ToString(CultureInfo.InvariantCulture),
            "+")
        : count.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Сколько ждёт очередь: «ждёт 3 дня», «ждёт 2 часа», «только что».
    /// </summary>
    /// <param name="awaitingSince">Момент попадания в очередь.</param>
    /// <param name="now">Текущий момент — параметром, чтобы тест не ждал часов.</param>
    /// <returns>Фраза об ожидании.</returns>
    public static string WaitingSince(DateTimeOffset awaitingSince, DateTimeOffset now)
    {
        var waited = WaitedFor(now - awaitingSince);

        return waited.Length == 0 ? Loc.T("только что") : Loc.T("ждёт {0}", waited);
    }

    /// <summary>
    /// Подпись самого старого отзыва очереди: «Самый старый ждёт 3 дня».
    /// </summary>
    /// <remarks>
    /// Слово «ждёт» не ставится перед «только что»: «Самый старый — только что» читается,
    /// а «Самый старый ждёт только что» — нет.
    /// </remarks>
    /// <param name="awaitingSince">Момент попадания отзыва в очередь.</param>
    /// <param name="now">Текущий момент.</param>
    /// <returns>Готовая строка для строки раздела.</returns>
    public static string OldestReview(DateTimeOffset awaitingSince, DateTimeOffset now)
    {
        var waited = WaitedFor(now - awaitingSince);

        return waited.Length == 0
            ? Loc.T("Самый старый — только что")
            : Loc.T("Самый старый ждёт {0}", waited);
    }

    /// <summary>
    /// Время снимка сводки в поясе устройства: «Обновлено в 14:32».
    /// </summary>
    /// <param name="moment">Момент чтения сводки.</param>
    /// <returns>Подпись под заголовком раздела.</returns>
    public static string UpdatedAt(DateTimeOffset moment) => Loc.T(
        "Обновлено в {0}",
        moment.ToLocalTime().ToString("HH:mm", Loc.Culture));

    /// <summary>
    /// Длительность ожидания словами; пустая строка — меньше минуты.
    /// </summary>
    private static string WaitedFor(TimeSpan difference)
    {
        if (difference < TimeSpan.Zero)
        {
            difference = TimeSpan.Zero;
        }

        var minutes = (int)difference.TotalMinutes;

        if (minutes < 1)
        {
            return string.Empty;
        }

        if (minutes < 60)
        {
            return Loc.Plural(PluralKey.Minutes, minutes);
        }

        var hours = minutes / 60;

        return hours < 24
            ? Loc.Plural(PluralKey.Hours, hours)
            : Loc.Plural(PluralKey.Days, hours / 24);
    }
}
