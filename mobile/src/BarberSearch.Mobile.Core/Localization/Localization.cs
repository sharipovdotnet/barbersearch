using System.Globalization;
using System.Text.Json;

namespace BarberSearch.Mobile.Core.Localization;

/// <summary>
/// Английский каталог: пары «русская строка → английская».
/// </summary>
public interface ITranslationCatalog
{
    /// <summary>
    /// Ищет перевод по русскому исходному тексту.
    /// </summary>
    /// <param name="source">Русская строка — ключ каталога.</param>
    /// <param name="translation">Английский текст.</param>
    /// <returns><c>true</c>, если перевод есть.</returns>
    bool TryGet(string source, out string translation);
}

/// <summary>
/// Каталог перевода из встроенного JSON.
/// </summary>
/// <remarks>
/// Формат повторяет веб: ключ — русский исходный текст, значение — английский. JSON лежит
/// встроенным ресурсом рядом с кодом, поэтому каталог работает и на Android, и на iOS без
/// файловых операций во время выполнения. Ошибка чтения — ошибка сборки, а не экрана:
/// без каталога английский интерфейс молча остался бы русским.
/// </remarks>
internal sealed class EmbeddedTranslationCatalog : ITranslationCatalog
{
    private const string ResourceName =
        "BarberSearch.Mobile.Core.Localization.messages.en.json";

    private readonly IReadOnlyDictionary<string, string> _messages;

    private EmbeddedTranslationCatalog(IReadOnlyDictionary<string, string> messages) =>
        _messages = messages;

    /// <summary>
    /// Загружает каталог из ресурса сборки.
    /// </summary>
    /// <returns>Готовый каталог.</returns>
    public static EmbeddedTranslationCatalog Load()
    {
        using var stream = typeof(EmbeddedTranslationCatalog).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Не найден встроенный ресурс {ResourceName}.");

        var messages = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException(
                $"Ресурс {ResourceName} пуст или имеет неверный формат.");

        return new EmbeddedTranslationCatalog(messages);
    }

    /// <inheritdoc />
    public bool TryGet(string source, out string translation) =>
        _messages.TryGetValue(source, out translation!);
}

/// <summary>
/// Перевод строк интерфейса и культура показа.
/// </summary>
public interface ILocalization
{
    /// <summary>
    /// Текущий язык интерфейса.
    /// </summary>
    Locale Locale { get; }

    /// <summary>
    /// Культура текущего языка — для дат, чисел и цен.
    /// </summary>
    CultureInfo Culture { get; }

    /// <summary>
    /// Язык изменён; UI пересобирается, поэтому событие нужно приложению, а не биндингам.
    /// </summary>
    event EventHandler? LocaleChanged;

    /// <summary>
    /// Читает сохранённый язык и каталог. Вызывается один раз на старте.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Меняет язык и сохраняет выбор.
    /// </summary>
    /// <param name="locale">Новый язык.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task SetLocaleAsync(Locale locale, CancellationToken cancellationToken = default);

    /// <summary>
    /// Переводит строку без параметров.
    /// </summary>
    /// <param name="source">Русский исходный текст.</param>
    /// <returns>Текст на текущем языке.</returns>
    string T(string source);

    /// <summary>
    /// Переводит строку и подставляет параметры <c>{0}</c>, <c>{1}</c>…
    /// </summary>
    /// <param name="source">Русский исходный текст.</param>
    /// <param name="args">Значения для подстановки.</param>
    /// <returns>Текст на текущем языке.</returns>
    string T(string source, params object?[] args);

    /// <summary>
    /// Счётчик в правильной форме для текущего языка.
    /// </summary>
    /// <param name="key">Смысловой ключ счётчика.</param>
    /// <param name="count">Число.</param>
    /// <returns>Число и слово в правильной форме.</returns>
    string Plural(PluralKey key, int count);
}

/// <summary>
/// Реализация перевода: каталог, выбранный язык и хранилище выбора.
/// </summary>
/// <remarks>
/// <para>
/// Подход gettext, как в вебе (<c>frontend/src/i18n/translate.ts</c>): ключ — русская
/// строка, поэтому непереведённая строка остаётся русской, а не превращается в ключ.
/// </para>
/// <para>
/// Параметры подставляются через <see cref="string.Format(IFormatProvider, string, object?[])"/>
/// с культурой языка: порядок <c>{0}</c> в английском переводе может отличаться от русского.
/// </para>
/// </remarks>
public sealed class Localization : ILocalization
{
    private static readonly Dictionary<PluralKey, PluralForms> RussianPlurals =
        new()
        {
            [PluralKey.Reviews] = new("{0} отзыв", "{0} отзыва", "{0} отзывов", null),
            [PluralKey.Experience] = new("{0} год", "{0} года", "{0} лет", null),
            [PluralKey.Minutes] = new("{0} минуту", "{0} минуты", "{0} минут", null),
            [PluralKey.Hours] = new("{0} час", "{0} часа", "{0} часов", null),
            [PluralKey.Days] = new("{0} день", "{0} дня", "{0} дней", null),
            [PluralKey.ShownBarbers] = new(
                "Показан {0} мастер",
                "Показаны {0} мастера",
                "Показаны {0} мастеров",
                null),
            [PluralKey.Profiles] = new("{0} профиль", "{0} профиля", "{0} профилей", null),
            [PluralKey.Requests] = new("{0} заявка", "{0} заявки", "{0} заявок", null),
        };

    private static readonly Dictionary<PluralKey, PluralForms> EnglishPlurals =
        new()
        {
            [PluralKey.Reviews] = new("{0} review", null, null, "{0} reviews"),
            [PluralKey.Experience] = new("{0} year", null, null, "{0} years"),
            [PluralKey.Minutes] = new("{0} minute", null, null, "{0} minutes"),
            [PluralKey.Hours] = new("{0} hour", null, null, "{0} hours"),
            [PluralKey.Days] = new("{0} day", null, null, "{0} days"),
            [PluralKey.ShownBarbers] = new(
                "Showing {0} barber",
                null,
                null,
                "Showing {0} barbers"),
            [PluralKey.Profiles] = new("{0} profile", null, null, "{0} profiles"),
            [PluralKey.Requests] = new("{0} request", null, null, "{0} requests"),
        };

    private readonly ILocaleStore _store;
    private readonly ITranslationCatalog _catalog;
    private readonly Locale? _initialLocale;
    private Locale _locale = Locale.Ru;

    /// <summary>
    /// Создаёт сервис перевода.
    /// </summary>
    /// <param name="store">Хранилище выбора языка.</param>
    /// <param name="catalog">Каталог; по умолчанию встроенный английский.</param>
    /// <param name="initialLocale">Язык вместо сохранённого — для тестов и дизайнера.</param>
    public Localization(
        ILocaleStore store,
        ITranslationCatalog? catalog = null,
        Locale? initialLocale = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _catalog = catalog ?? EmbeddedTranslationCatalog.Load();
        _initialLocale = initialLocale;
    }

    /// <inheritdoc />
    public Locale Locale => _locale;

    /// <inheritdoc />
    public CultureInfo Culture => Locales.Culture(_locale);

    /// <inheritdoc />
    public event EventHandler? LocaleChanged;

    /// <inheritdoc />
    public void Initialize() => _locale = _initialLocale ?? _store.Read();

    /// <inheritdoc />
    public Task SetLocaleAsync(
        Locale locale,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_locale == locale)
        {
            return Task.CompletedTask;
        }

        _locale = locale;
        _store.Write(locale);
        LocaleChanged?.Invoke(this, EventArgs.Empty);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public string T(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return Text(source);
    }

    /// <inheritdoc />
    public string T(string source, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(args);

        var message = Text(source);

        return args.Length == 0 ? message : string.Format(Culture, message, args);
    }

    /// <inheritdoc />
    public string Plural(PluralKey key, int count)
    {
        var forms = _locale == Locale.En
            ? EnglishPlurals[key]
            : RussianPlurals[key];

        var category = _locale == Locale.En
            ? count == 1 ? PluralCategory.One : PluralCategory.Other
            : RussianCategory(count);

        var template = category switch
        {
            PluralCategory.One => forms.One,
            PluralCategory.Few => forms.Few ?? forms.Many ?? forms.Other ?? forms.One,
            PluralCategory.Many => forms.Many ?? forms.Few ?? forms.Other ?? forms.One,
            _ => forms.Other ?? forms.Many ?? forms.Few ?? forms.One,
        };

        return string.Format(Culture, template, count);
    }

    private string Text(string source) =>
        _locale == Locale.En && _catalog.TryGet(source, out var translation)
            ? translation
            : source;

    private static PluralCategory RussianCategory(int count)
    {
        var rest = Math.Abs(count) % 100;
        var last = rest % 10;

        if (rest is >= 11 and <= 14)
        {
            return PluralCategory.Many;
        }

        return last switch
        {
            1 => PluralCategory.One,
            2 or 3 or 4 => PluralCategory.Few,
            _ => PluralCategory.Many,
        };
    }

    private enum PluralCategory
    {
        One,
        Few,
        Many,
        Other,
    }

    private sealed record PluralForms(
        string One,
        string? Few,
        string? Many,
        string? Other);
}

/// <summary>
/// Текущий сервис перевода для мест, где внедрение зависимости недоступно.
/// </summary>
/// <remarks>
/// <para>
/// XAML-разметка и статические форматтеры не получают конструктор, поэтому им нужен один
/// модульный держатель — как <c>currentLocale</c> в вебе. Приложение выставляет его в
/// <c>MauiProgram</c>, тесты — своим экземпляром.
/// </para>
/// <para>
/// Смена языка не обновляет привязки: приложение пересобирает Shell целиком, и все
/// страницы и ViewModel создаются заново с новым языком. Это осознанное отличие от веба,
/// где React перерисовывает дерево; на телефоне пересборка дешевле, чем событие в каждом
/// ViewModel.
/// </para>
/// </remarks>
public static class Loc
{
    private static ILocalization _current = CreateDefault();

    /// <summary>
    /// Текущий сервис перевода.
    /// </summary>
    public static ILocalization Current
    {
        get => _current;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _current = value;
        }
    }

    /// <summary>
    /// Текущий язык.
    /// </summary>
    public static Locale Locale => _current.Locale;

    /// <summary>
    /// Культура текущего языка.
    /// </summary>
    public static CultureInfo Culture => _current.Culture;

    /// <summary>
    /// Перевод строки без параметров.
    /// </summary>
    /// <param name="source">Русский исходный текст.</param>
    /// <returns>Текст на текущем языке.</returns>
    public static string T(string source) => _current.T(source);

    /// <summary>
    /// Перевод строки с параметрами.
    /// </summary>
    /// <param name="source">Русский исходный текст.</param>
    /// <param name="args">Значения для подстановки.</param>
    /// <returns>Текст на текущем языке.</returns>
    public static string T(string source, params object?[] args) =>
        _current.T(source, args);

    /// <summary>
    /// Счётчик в правильной форме.
    /// </summary>
    /// <param name="key">Смысловой ключ счётчика.</param>
    /// <param name="count">Число.</param>
    /// <returns>Число и слово в правильной форме.</returns>
    public static string Plural(PluralKey key, int count) =>
        _current.Plural(key, count);

    private static Localization CreateDefault()
    {
        var localization = new Localization(new InMemoryLocaleStore());
        localization.Initialize();

        return localization;
    }
}
