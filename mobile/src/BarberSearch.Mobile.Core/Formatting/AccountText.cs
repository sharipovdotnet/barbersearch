using System.Globalization;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Domain.Users;

namespace BarberSearch.Mobile.Core.Formatting;

/// <summary>
/// Тексты аккаунта: роль, полное имя, инициалы и служебные моменты профиля.
/// </summary>
/// <remarks>
/// <para>
/// Подписи ролей взяты из веба (<c>frontend/src/lib/my-profile.ts</c>,
/// <c>ACCOUNT_ROLE_LABELS</c>): значения API остаются английскими, русская подпись живёт здесь.
/// Полное имя, инициалы и «Ещё не было» повторяют <c>profileDisplayName</c>,
/// <c>getInitials</c> и карточку «Аккаунт».
/// </para>
/// <para>
/// Служебные моменты (<c>createdAt</c>, <c>lastLoginAt</c>) показываются в поясе устройства:
/// у них нет салона, к которому их можно было бы привязать, поэтому это эквивалент
/// браузерного пояса веба. Метод принимает пояс параметром — так тест фиксирует результат,
/// а экран передаёт <see cref="TimeZoneInfo.Local"/>.
/// </para>
/// </remarks>
public static class AccountText
{
    /// <summary>
    /// Значение поля, у которого ещё нет данных: последнего входа не было.
    /// </summary>
    public const string Never = "Ещё не было";

    /// <summary>
    /// Подпись имени, когда его нет ни в профиле, ни в email.
    /// </summary>
    public const string FallbackName = "Профиль";

    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>
    /// Русские подписи ролей; значения API остаются английскими.
    /// </summary>
    private static readonly Dictionary<UserRole, string> Roles = new()
    {
        [UserRole.Customer] = "Клиент",
        [UserRole.Barber] = "Барбер",
        [UserRole.Admin] = "Администратор",
    };

    /// <summary>
    /// Подпись роли.
    /// </summary>
    /// <param name="role">Роль из профиля.</param>
    /// <returns>Русская подпись; неизвестная роль показывается как есть.</returns>
    public static string RoleText(UserRole role) =>
        Roles.TryGetValue(role, out var text) ? text : role.ToString();

    /// <summary>
    /// Полное имя для шапки профиля: «Ева Клиентова».
    /// </summary>
    /// <remarks>
    /// Пустые имя и фамилия — не повод показывать пустую строку: профиль без имени мог быть
    /// создан администратором, и тогда виден email — он у аккаунта есть всегда.
    /// </remarks>
    /// <param name="profile">Профиль из базы.</param>
    /// <returns>Имя и фамилия либо email.</returns>
    public static string DisplayName(MyProfileResponse profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var name = string.Join(
            ' ',
            new[] { profile.FirstName, profile.LastName }
                .Select(part => part?.Trim())
                .Where(part => !string.IsNullOrEmpty(part)));

        if (name.Length > 0)
        {
            return name;
        }

        return string.IsNullOrWhiteSpace(profile.Email) ? FallbackName : profile.Email.Trim();
    }

    /// <summary>
    /// Инициалы для круглой подписи: «ЕК», у email — первая буква.
    /// </summary>
    /// <remarks>
    /// Фотографии в этом срезе нет, поэтому аватар — две буквы имени. Email даёт одну букву:
    /// разбивать «eva@example.com» по словам значило бы взять «E» и «@».
    /// </remarks>
    /// <param name="profile">Профиль из базы.</param>
    /// <returns>Одна или две буквы в верхнем регистре.</returns>
    public static string Initials(MyProfileResponse profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var words = new[] { profile.FirstName, profile.LastName }
            .Select(part => part?.Trim())
            .Where(part => !string.IsNullOrEmpty(part))
            .Select(part => part!)
            .ToList();

        if (words.Count == 0)
        {
            var email = profile.Email?.Trim();

            return string.IsNullOrEmpty(email) ? "?" : TakeFirstLetter(email);
        }

        var letters = words.Count == 1 ? [words[0]] : new[] { words[0], words[1] };

        return string.Concat(letters.Select(TakeFirstLetter));
    }

    /// <summary>
    /// Служебный момент аккаунта в поясе устройства: «сб, 15 ноября в 18:30».
    /// </summary>
    /// <remarks>
    /// Формат повторяет <c>formatDateTime</c> веба: день недели, число, месяц и время.
    /// Года нет намеренно — он есть у отзыва, а «когда создан аккаунт» и «когда входил
    /// последний раз» живут в пределах года работы с продуктом. Культура задана явно:
    /// настройки телефона в другой локали дали бы «15 Nov».
    /// </remarks>
    /// <param name="instant">Момент из ответа API.</param>
    /// <param name="timeZone">Пояс устройства, в котором показывать момент.</param>
    /// <returns>Дата и время.</returns>
    public static string Moment(DateTimeOffset instant, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var local = TimeZoneInfo.ConvertTime(instant, timeZone);

        return string.Concat(
            local.ToString("ddd, d MMMM", RuCulture),
            " в ",
            local.ToString("HH:mm", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Служебный момент либо «Ещё не было».
    /// </summary>
    /// <param name="instant">Момент из ответа API или <c>null</c>.</param>
    /// <param name="timeZone">Пояс устройства.</param>
    /// <returns>Дата и время либо подпись отсутствия.</returns>
    public static string MomentOrNever(DateTimeOffset? instant, TimeZoneInfo timeZone) =>
        instant is null ? Never : Moment(instant.Value, timeZone);

    /// <summary>
    /// Берёт первую букву слова в верхнем регистре.
    /// </summary>
    /// <remarks>
    /// Первая буква — символьная единица строки: суррогатная пара (эмодзи в имени) иначе
    /// разрезалась бы пополам и дала бы непечатаемый символ.
    /// </remarks>
    private static string TakeFirstLetter(string word)
    {
        if (word.Length == 0)
        {
            return string.Empty;
        }

        var letter = char.IsHighSurrogate(word[0]) && word.Length > 1
            ? word[..2]
            : word[..1];

        return letter.ToUpper(RuCulture);
    }
}
