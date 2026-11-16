namespace BarberSearch.Application.Addresses;

/// <summary>
/// Настройки подсказок города и адреса — секция конфигурации <c>AddressSuggestions</c>.
/// </summary>
/// <remarks>
/// <para>
/// Заполняется композиционным корнем Web (после проверки диапазонов в <c>StartupSettings</c>)
/// и регистрируется синглтоном. Application не знает ни <c>IConfiguration</c>, ни
/// <c>IOptions</c>: обработчики получают готовый объект.
/// </para>
/// <para>
/// Тип живёт в Application, а не в инфраструктуре: границы запроса (<see cref="MinQueryLength"/>,
/// <see cref="MinCityQueryLength"/>, <see cref="MaxSuggestions"/>) нужны обработчикам, а
/// Application не может ссылаться на Infrastructure — зависимость идёт только в обратную сторону.
/// </para>
/// <para>
/// <see cref="Enabled"/> по умолчанию <c>false</c>: без ключа провайдера фича выключена, старт
/// не падает, а оба эндпоинта отвечают <c>409 addressSuggestions.unavailable</c>. Выключенная
/// фича не вызывает порт вообще — иначе её поведение зависело бы от того, какой адаптер
/// зарегистрирован в композиционном корне.
/// </para>
/// <para>
/// <see cref="IsConfigured"/> проверяет только абсолютность <see cref="BaseUrl"/>: схему
/// <c>https</c> проверяет <c>StartupSettings</c> и только в <c>Production</c>, иначе локальный
/// стаб по <c>http</c> было бы не проверить. Ключ не валидируется на «правильность» — <c>401</c>
/// провайдера это ошибка запроса, а не падение старта.
/// </para>
/// </remarks>
public sealed record AddressSuggestionsOptions
{
    /// <summary>
    /// Имя секции конфигурации: <c>AddressSuggestions</c>.
    /// </summary>
    public const string SECTION = "AddressSuggestions";

    /// <summary>
    /// Адрес API подсказок по умолчанию: метод <c>suggest/address</c> DaData.
    /// </summary>
    public const string DEFAULT_BASE_URL =
        "https://suggestions.dadata.ru/suggestions/api/4_1/rs/suggest/address";

    /// <summary>
    /// Таймаут одного вызова провайдера по умолчанию, секунды.
    /// </summary>
    public const int DEFAULT_TIMEOUT_SECONDS = 5;

    /// <summary>
    /// Минимальная длина запроса адреса по умолчанию.
    /// </summary>
    public const int DEFAULT_MIN_QUERY_LENGTH = 3;

    /// <summary>
    /// Минимальная длина запроса города по умолчанию: города бывают короче трёх букв («Уфа»).
    /// </summary>
    public const int DEFAULT_MIN_CITY_QUERY_LENGTH = 2;

    /// <summary>
    /// Сколько подсказок отдаётся по умолчанию и максимум.
    /// </summary>
    public const int DEFAULT_MAX_SUGGESTIONS = 8;

    /// <summary>
    /// Время жизни кэша ответов провайдера по умолчанию, секунды.
    /// </summary>
    public const int DEFAULT_CACHE_TTL_SECONDS = 900;

    /// <summary>
    /// Включена ли фича; <c>AddressSuggestions:Enabled</c>, по умолчанию <c>false</c>.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Адрес API подсказок; <c>AddressSuggestions:BaseUrl</c>, по умолчанию — DaData.
    /// </summary>
    public string? BaseUrl { get; init; } = DEFAULT_BASE_URL;

    /// <summary>
    /// Ключ провайдера; <c>AddressSuggestions:ApiKey</c>. Секрет: только user secrets или
    /// переменные окружения, никогда не <c>appsettings.json</c>.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Таймаут одного вызова в секундах; <c>AddressSuggestions:TimeoutSeconds</c>, по умолчанию 5.
    /// </summary>
    public int TimeoutSeconds { get; init; } = DEFAULT_TIMEOUT_SECONDS;

    /// <summary>
    /// Минимальная длина запроса адреса; <c>AddressSuggestions:MinQueryLength</c>, по умолчанию 3.
    /// </summary>
    /// <remarks>
    /// Порог проверяется в обработчике, а не моделью запроса: клиент получает доменный код
    /// <c>addressSuggestions.query.tooShort</c>, а не framework-<c>400</c>.
    /// </remarks>
    public int MinQueryLength { get; init; } = DEFAULT_MIN_QUERY_LENGTH;

    /// <summary>
    /// Минимальная длина запроса города; <c>AddressSuggestions:MinCityQueryLength</c>,
    /// по умолчанию 2.
    /// </summary>
    /// <remarks>
    /// Порог ниже, чем у адреса: город может называться «Аша» или «Уфа», и три символа
    /// отрезали бы половину коротких названий.
    /// </remarks>
    public int MinCityQueryLength { get; init; } = DEFAULT_MIN_CITY_QUERY_LENGTH;

    /// <summary>
    /// Сколько подсказок вернуть; <c>AddressSuggestions:MaxSuggestions</c>, по умолчанию 8.
    /// </summary>
    /// <remarks>
    /// Одновременно значение по умолчанию и верхняя граница <c>limit</c>: больше подсказок,
    /// чем в выпадающем списке, всё равно не покажут.
    /// </remarks>
    public int MaxSuggestions { get; init; } = DEFAULT_MAX_SUGGESTIONS;

    /// <summary>
    /// Время жизни кэша ответов; <c>AddressSuggestions:CacheTtlSeconds</c>, по умолчанию 900.
    /// </summary>
    /// <remarks>
    /// <c>0</c> выключает кэш: каждый запрос уходит провайдеру.
    /// </remarks>
    public int CacheTtlSeconds { get; init; } = DEFAULT_CACHE_TTL_SECONDS;

    /// <summary>
    /// Настроена ли фича: включена, ключ не пуст, <see cref="BaseUrl"/> — абсолютный URI.
    /// </summary>
    /// <remarks>
    /// Схема здесь не проверяется: <c>StartupSettings</c> требует <c>https</c> только
    /// в <c>Production</c>, чтобы локальный стаб по <c>http</c> оставался проверяемым.
    /// </remarks>
    public bool IsConfigured => Enabled
        && !string.IsNullOrWhiteSpace(ApiKey)
        && Uri.TryCreate(BaseUrl, UriKind.Absolute, out _);

    /// <summary>
    /// Печатает только несекретные флаги: ключ и его адрес в журнал не попадают.
    /// </summary>
    public override string ToString()
        => $"AddressSuggestionsOptions {{ Enabled = {Enabled}, IsConfigured = {IsConfigured} }}";
}
