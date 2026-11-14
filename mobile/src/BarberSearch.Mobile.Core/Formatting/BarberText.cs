using System.Globalization;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.Salons;

namespace BarberSearch.Mobile.Core.Formatting;

/// <summary>
/// Тексты карточки барбера: грейд, место приёма, цена, длительность, отзывы.
/// </summary>
/// <remarks>
/// <para>
/// Все подписи взяты из веба (<c>frontend/src/lib/barber-grade.ts</c>,
/// <c>frontend/src/lib/place-kind.ts</c>, <c>frontend/src/lib/format.ts</c>) и собраны здесь,
/// чтобы экран не форматировал строки сам: одна копия текста на два клиента.
/// </para>
/// <para>
/// Культура задана явно (<c>ru-RU</c>): приложение русскоязычное, а настройки устройства
/// в другой локали дали бы «2,400 ₽» вместо «2 400 ₽».
/// </para>
/// </remarks>
public static class BarberText
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>
    /// Русские подписи грейдов; значения API остаются английскими.
    /// </summary>
    private static readonly Dictionary<Grade, string> Grades = new()
    {
        [Grade.None] = "Без грейда",
        [Grade.Intern] = "Стажёр",
        [Grade.JuniorBarber] = "Младший барбер",
        [Grade.MiddleBarber] = "Барбер",
        [Grade.SeniorBarber] = "Старший барбер",
        [Grade.ArtDirector] = "Арт-директор",
        [Grade.BarberSchoolTrainer] = "Тренер школы",
        [Grade.Manager] = "Управляющий",
        [Grade.CompetitionBarber] = "Соревновательный барбер",
        [Grade.BrandAmbassador] = "Амбассадор бренда",
        [Grade.BarbershopOwner] = "Владелец барбершопа",
    };

    /// <summary>
    /// Русские подписи вида места приёма.
    /// </summary>
    private static readonly Dictionary<PlaceKind, string> PlaceKinds = new()
    {
        [PlaceKind.Salon] = "Барбершоп",
        [PlaceKind.RentalStudio] = "Студия",
        [PlaceKind.SoloStudio] = "Кабинет",
        [PlaceKind.Home] = "На дому",
        [PlaceKind.Mobile] = "Выезд",
    };

    /// <summary>
    /// Подпись грейда; неизвестное значение показывается как «Без грейда».
    /// </summary>
    /// <param name="grade">Грейд из карточки.</param>
    /// <returns>Русская подпись.</returns>
    public static string GradeText(Grade grade) =>
        Grades.TryGetValue(grade, out var text) ? text : "Без грейда";

    /// <summary>
    /// Короткая подпись вида места: «Барбершоп», «Кабинет», «На дому».
    /// </summary>
    /// <param name="kind">Вид места приёма.</param>
    /// <returns>Русская подпись.</returns>
    public static string PlaceKindText(PlaceKind kind) =>
        PlaceKinds.TryGetValue(kind, out var text) ? text : "Место приёма";

    /// <summary>
    /// Место приёма в шапке карточки: название, город и вид места.
    /// </summary>
    /// <remarks>
    /// Для приёма на дому названия места у клиента нет — показывается фраза с городом;
    /// для выезда — «Выезд по городу». Остальные места собираются как в вебе:
    /// «Название · Город · Вид».
    /// </remarks>
    /// <param name="kind">Вид места приёма.</param>
    /// <param name="salonName">Название салона; пусто у домашнего приёма.</param>
    /// <param name="city">Город салона.</param>
    /// <returns>Строка для шапки.</returns>
    public static string Place(PlaceKind kind, string? salonName, string? city)
    {
        var placeKind = PlaceKindText(kind);

        if (kind == PlaceKind.Home)
        {
            return string.IsNullOrWhiteSpace(city) ? placeKind : string.Concat("На дому в ", city);
        }

        if (kind == PlaceKind.Mobile)
        {
            return "Выезд по городу";
        }

        var place = string.Join(
            " · ",
            new[] { salonName, city }.Where(part => !string.IsNullOrWhiteSpace(part)));

        return string.IsNullOrWhiteSpace(place)
            ? placeKind
            : string.Concat(place, " · ", placeKind);
    }

    /// <summary>
    /// Цена: «2 400 ₽», копейки только если они есть.
    /// </summary>
    /// <remarks>
    /// Неразрывный пробел перед знаком рубля и между разрядами — как у <c>Intl.NumberFormat</c>
    /// в вебе: цена не должна разрываться переносом строки.
    /// </remarks>
    /// <param name="value">Цена из прайса.</param>
    /// <returns>Строка цены.</returns>
    public static string Price(decimal value)
    {
        var format = value % 1m == 0m ? "#,##0" : "#,##0.##";

        return string.Concat(value.ToString(format, RuCulture), "\u00A0₽");
    }

    /// <summary>
    /// Длительность услуги: «45 мин», «1 ч», «1 ч 30 мин».
    /// </summary>
    /// <param name="totalMinutes">Длительность в минутах.</param>
    /// <returns>Строка длительности; пустая для неположительной.</returns>
    public static string Duration(int totalMinutes)
    {
        if (totalMinutes <= 0)
        {
            return string.Empty;
        }

        if (totalMinutes < 60)
        {
            return string.Concat(totalMinutes.ToString(CultureInfo.InvariantCulture), " мин");
        }

        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        var hoursText = string.Concat(hours.ToString(CultureInfo.InvariantCulture), " ч");

        return minutes == 0
            ? hoursText
            : string.Concat(
                hoursText,
                " ",
                minutes.ToString(CultureInfo.InvariantCulture),
                " мин");
    }

    /// <summary>
    /// Количество отзывов в правильной форме: «1 отзыв», «2 отзыва», «12 отзывов».
    /// </summary>
    /// <param name="count">Число опубликованных отзывов.</param>
    /// <returns>Строка счётчика.</returns>
    public static string Reviews(int count)
    {
        var rest = Math.Abs(count) % 100;
        var last = rest % 10;

        var form = rest is >= 11 and <= 14
            ? "отзывов"
            : last switch
            {
                1 => "отзыв",
                2 or 3 or 4 => "отзыва",
                _ => "отзывов",
            };

        return string.Concat(count.ToString(CultureInfo.InvariantCulture), " ", form);
    }

    /// <summary>
    /// Рейтинг с одним знаком после запятой; <c>null</c> — прочерк.
    /// </summary>
    /// <param name="value">Средний рейтинг 1.00..5.00.</param>
    /// <returns>«4,8» или «—».</returns>
    public static string Rating(decimal? value) =>
        value is null ? "—" : value.Value.ToString("0.0", RuCulture);

    /// <summary>
    /// Подпись подтверждённого профиля.
    /// </summary>
    public const string Verified = "Проверен";

    /// <summary>
    /// Русские подписи языков общения: самоназвания, как в вебе, а не перевод.
    /// </summary>
    private static readonly Dictionary<Language, string> Languages = new()
    {
        [Language.Ru] = "Русский",
        [Language.En] = "English",
        [Language.De] = "Deutsch",
        [Language.Kg] = "Кыргызча",
        [Language.Tj] = "Тоҷикӣ",
        [Language.Uz] = "Oʻzbekcha",
    };

    /// <summary>
    /// Русские подписи способов оплаты.
    /// </summary>
    private static readonly Dictionary<PaymentMethod, string> PaymentMethods = new()
    {
        [PaymentMethod.Cash] = "Наличные",
        [PaymentMethod.Card] = "Карта",
        [PaymentMethod.DigitalWallet] = "Цифровой кошелёк",
        [PaymentMethod.Sbp] = "СБП",
        [PaymentMethod.BankTransfer] = "Перевод",
    };

    /// <summary>
    /// Стаж в правильной форме: «1 год», «2 года», «8 лет».
    /// </summary>
    /// <remarks>
    /// Формы повторяют плюрал <c>experience</c> веба: 11–14 всегда «лет», дальше — по
    /// последней цифре. Отрицательный стаж не бывает, но берётся модуль, чтобы строка
    /// не сломалась на мусорных данных.
    /// </remarks>
    /// <param name="years">Стаж в годах.</param>
    /// <returns>Число и форма слова.</returns>
    public static string Experience(int years)
    {
        var rest = Math.Abs(years) % 100;
        var last = rest % 10;

        var form = rest is >= 11 and <= 14
            ? "лет"
            : last switch
            {
                1 => "год",
                2 or 3 or 4 => "года",
                _ => "лет",
            };

        return string.Concat(years.ToString(CultureInfo.InvariantCulture), " ", form);
    }

    /// <summary>
    /// Подпись языка: «Русский», «English», «Кыргызча».
    /// </summary>
    /// <param name="language">Язык из карточки.</param>
    /// <returns>Самоназвание языка.</returns>
    public static string LanguageText(Language language) =>
        Languages.TryGetValue(language, out var text) ? text : language.ToString();

    /// <summary>
    /// Подпись способа оплаты: «Наличные», «Карта», «СБП».
    /// </summary>
    /// <param name="method">Способ оплаты из карточки.</param>
    /// <returns>Русская подпись.</returns>
    public static string PaymentText(PaymentMethod method) =>
        PaymentMethods.TryGetValue(method, out var text) ? text : method.ToString();

    /// <summary>
    /// Инициалы для заглушки аватара: первые буквы первых двух слов.
    /// </summary>
    /// <remarks>
    /// Имя приходит от пользователя и может быть пустым или однословным; пустое имя даёт
    /// «?», как <c>getInitials</c> веба, — заглушка без буквы выглядела бы поломкой.
    /// <see cref="AccountText.Initials"/> здесь не подходит: он завязан на профиль кабинета.
    /// </remarks>
    /// <param name="displayName">Имя мастера из карточки.</param>
    /// <returns>Одна или две буквы в верхнем регистре.</returns>
    public static string Initials(string displayName)
    {
        var words = (displayName ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
        {
            return "?";
        }

        var initials = words
            .Take(2)
            .Select(word => char.ToUpperInvariant(word[0]));

        return new string([.. initials]);
    }

    /// <summary>
    /// Описание фотографии для экранного диктора.
    /// </summary>
    /// <remarks>
    /// Подпись владельца важнее шаблона: она описывает кадр, а шаблон — только его место
    /// в портфолио. Номер — с единицы, как в вебе.
    /// </remarks>
    /// <param name="barberName">Имя мастера.</param>
    /// <param name="number">Номер фотографии с единицы.</param>
    /// <param name="description">Подпись фотографии; может быть пустой.</param>
    /// <returns>Подпись либо шаблон «Работа барбера N, фото M».</returns>
    public static string PhotoAlt(string barberName, int number, string? description) =>
        string.IsNullOrWhiteSpace(description)
            ? string.Concat(
                "Работа барбера ",
                barberName ?? string.Empty,
                ", фото ",
                number.ToString(CultureInfo.InvariantCulture))
            : description.Trim();

    /// <summary>
    /// Подсказка нажатия на плитку портфолио: «Открыть фото: {подпись}».
    /// </summary>
    /// <param name="caption">Подпись фотографии.</param>
    /// <returns>Текст для <c>SemanticProperties.Hint</c>.</returns>
    public static string PhotoOpenHint(string caption) =>
        string.Concat("Открыть фото: ", caption ?? string.Empty);

    /// <summary>
    /// Счётчик кадров: «3 из 12».
    /// </summary>
    /// <param name="current">Номер текущего кадра с единицы.</param>
    /// <param name="total">Всего кадров.</param>
    /// <returns>Номер и общее число.</returns>
    public static string PhotoCounter(int current, int total) => string.Concat(
        current.ToString(CultureInfo.InvariantCulture),
        " из ",
        total.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Диапазон видимых строк страницы: «1–10 из 12».
    /// </summary>
    /// <remarks>
    /// Тире — среднее, как в макете. Смещение отсчитывается от нуля, а показывает диапазон
    /// с единицы; пустая страница даёт «0 из {total}», как <c>formatPageRange</c> веба.
    /// Общее число берётся из профиля, а не из длины страницы: последняя страница неполная.
    /// </remarks>
    /// <param name="offset">Сколько строк было на предыдущих страницах.</param>
    /// <param name="count">Сколько строк на текущей странице.</param>
    /// <param name="total">Всего строк по данным профиля.</param>
    /// <returns>Диапазон и общее число.</returns>
    public static string PageRange(int offset, int count, int total)
    {
        var totalText = total.ToString(CultureInfo.InvariantCulture);

        if (count <= 0)
        {
            return string.Concat("0 из ", totalText);
        }

        return string.Concat(
            (offset + 1).ToString(CultureInfo.InvariantCulture),
            "–",
            (offset + count).ToString(CultureInfo.InvariantCulture),
            " из ",
            totalText);
    }
}
