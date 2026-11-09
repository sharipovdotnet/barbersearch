namespace BarberSearch.Application.Emails;

/// <summary>
/// Настройки transactional outbox для писем — секция конфигурации <c>EmailOutbox</c>.
/// </summary>
/// <remarks>
/// <para>
/// Заполняется композиционным корнем Web: значения проверяет <c>StartupSettings</c>, после
/// чего готовый объект регистрируется синглтоном. Application читает только значения — ни
/// <c>IConfiguration</c>, ни <c>IOptions</c> в слой не проникают.
/// </para>
/// <para>
/// <see cref="Enabled"/> управляет только фоновым воркером: обработчики запросов пишут строку
/// в outbox всегда, когда почта настроена. Поэтому выключенный воркер не теряет письма —
/// они копятся в таблице и уходят после включения флага, без перезапуска процесса.
/// </para>
/// <para>
/// <see cref="RetentionDays"/> ограничивает хранение уже обработанных строк: тело письма
/// содержит открытый токен, и держать его дольше, чем нужно для доставки, нельзя.
/// </para>
/// </remarks>
public sealed record EmailOutboxOptions
{
    /// <summary>
    /// Имя секции конфигурации: <c>EmailOutbox</c>.
    /// </summary>
    public const string SECTION = "EmailOutbox";

    /// <summary>
    /// Размер пачки по умолчанию: сколько строк диспетчер забирает за один тик.
    /// </summary>
    public const int DEFAULT_BATCH_SIZE = 20;

    /// <summary>
    /// Интервал тика по умолчанию в секундах.
    /// </summary>
    public const int DEFAULT_INTERVAL_SECONDS = 30;

    /// <summary>
    /// Предел попыток по умолчанию: после пятой неудачи строка получает <c>failed_at</c>.
    /// </summary>
    public const int DEFAULT_MAX_ATTEMPTS = 5;

    /// <summary>
    /// Ретенция обработанных строк по умолчанию в днях.
    /// </summary>
    public const int DEFAULT_RETENTION_DAYS = 7;

    /// <summary>
    /// Включён ли фоновый диспетчер. По умолчанию выключен: локальная разработка и CI
    /// не должны слать письма сами.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Размер пачки за один тик; <c>EmailOutbox:BatchSize</c>.
    /// </summary>
    public int BatchSize { get; init; } = DEFAULT_BATCH_SIZE;

    /// <summary>
    /// Интервал между тиками в секундах; <c>EmailOutbox:IntervalSeconds</c>.
    /// </summary>
    public int IntervalSeconds { get; init; } = DEFAULT_INTERVAL_SECONDS;

    /// <summary>
    /// Сколько раз пробовать отправку до окончательного отказа; <c>EmailOutbox:MaxAttempts</c>.
    /// </summary>
    public int MaxAttempts { get; init; } = DEFAULT_MAX_ATTEMPTS;

    /// <summary>
    /// Сколько дней хранить отправленные и окончательно проваленные строки;
    /// <c>EmailOutbox:RetentionDays</c>.
    /// </summary>
    public int RetentionDays { get; init; } = DEFAULT_RETENTION_DAYS;
}
