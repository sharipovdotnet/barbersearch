using BarberSearch.Domain;

namespace BarberSearch.Application.Push;

/// <summary>
/// Настройки push-канала — секция конфигурации <c>Push</c>.
/// </summary>
/// <remarks>
/// <para>
/// Заполняется композиционным корнем Web (после проверки диапазонов в <c>StartupSettings</c>)
/// и регистрируется синглтоном. Application не знает ни <c>IConfiguration</c>, ни
/// <c>IOptions</c>: обработчики получают готовый объект.
/// </para>
/// <para>
/// <see cref="Enabled"/> по умолчанию <c>false</c> — до поправки Политики конфиденциальности
/// (152-ФЗ: Google FCM — сторонний обработчик и трансграничная передача идентификатора
/// устройства) канал не должен работать в бою. В <c>appsettings.Development.json</c> флаг
/// включён без ключей: это «режим разработки» (<see cref="DevMode"/>) — текст собирается
/// и пишется в журнал, строка становится <c>Skipped</c>, а не зависает <c>Pending</c>.
/// В других окружениях такое состояние оставляет строки <c>Pending</c> и шлёт одно
/// предупреждение: напоминание не должно теряться из-за забытого ключа.
/// </para>
/// <para>
/// <see cref="IsConfigured"/> — «есть project id и разбираемый сервисный ключ». Ключ читается
/// один раз (лениво) из <see cref="ServiceAccountJson"/> либо из файла
/// <see cref="ServiceAccountPath"/>; сам секрет не валидируется в <c>StartupSettings</c>,
/// чтобы старт без ключей оставался штатным.
/// </para>
/// </remarks>
public sealed record PushOptions
{
    /// <summary>
    /// Имя секции конфигурации: <c>Push</c>.
    /// </summary>
    public const string SECTION = "Push";

    /// <summary>
    /// Интервал тика по умолчанию, секунды.
    /// </summary>
    public const int DEFAULT_INTERVAL_SECONDS = 60;

    /// <summary>
    /// Размер пачки доставки по умолчанию.
    /// </summary>
    public const int DEFAULT_BATCH_SIZE = 20;

    /// <summary>
    /// Окно создания напоминания по умолчанию: за 60 минут до визита.
    /// </summary>
    public const int DEFAULT_REMINDER_LEAD_MINUTES = 60;

    /// <summary>
    /// Бюджет попыток доставки по умолчанию.
    /// </summary>
    public const int DEFAULT_MAX_ATTEMPTS = Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS;

    /// <summary>
    /// Ретенция строк push-очереди по умолчанию: 30 дней.
    /// </summary>
    public const int DEFAULT_RETENTION_DAYS = 30;

    /// <summary>
    /// Ретенция токенов устройств по умолчанию: 180 дней без повторной регистрации.
    /// </summary>
    public const int DEFAULT_DEVICE_TOKEN_RETENTION_DAYS = 180;

    /// <summary>
    /// Предел активных устройств на пользователя по умолчанию.
    /// </summary>
    public const int DEFAULT_MAX_TOKENS_PER_USER = 5;

    private readonly Lazy<ServiceAccountKey?> _serviceAccountKey;

    /// <summary>
    /// Создаёт настройки с пустыми значениями: так же выглядит отсутствующая секция.
    /// </summary>
    /// <remarks>
    /// Ленивое чтение ключа начинается здесь, а не в свойстве, чтобы повторные проверки
    /// <see cref="IsConfigured"/> не читали файл заново.
    /// </remarks>
    public PushOptions()
        => _serviceAccountKey = new Lazy<ServiceAccountKey?>(LoadServiceAccountKey);

    /// <summary>
    /// Включён ли воркер push. По умолчанию выключен: ни строк, ни отправок.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Режим разработки: <c>Push:Enabled=true</c> без ключей превращает доставку в
    /// «собери текст, запиши в журнал и пометь строку <c>Skipped</c>».
    /// </summary>
    /// <remarks>
    /// Не ключ конфигурации: значение выставляет композиционный корень из
    /// <c>IHostEnvironment.IsDevelopment()</c>, чтобы в бою отсутствие ключей не «съедало»
    /// напоминания молча — там строки остаются <c>Pending</c> и ждут починки конфигурации.
    /// </remarks>
    public bool DevMode { get; init; }

    /// <summary>
    /// Интервал тика в секундах; <c>Push:IntervalSeconds</c>, по умолчанию 60.
    /// </summary>
    public int IntervalSeconds { get; init; } = DEFAULT_INTERVAL_SECONDS;

    /// <summary>
    /// Размер пачки доставки за тик; <c>Push:BatchSize</c>, по умолчанию 20.
    /// </summary>
    public int BatchSize { get; init; } = DEFAULT_BATCH_SIZE;

    /// <summary>
    /// За сколько минут до визита создаётся напоминание; <c>Push:ReminderLeadMinutes</c>,
    /// по умолчанию 60.
    /// </summary>
    /// <remarks>
    /// Это окно создания, а не текст сообщения: текст «Через час…» зафиксирован продуктом.
    /// Меняя окно, нужно менять и текст — здесь сознательно нет общей подстановки, чтобы
    /// «через 30 минут» не приходило под заголовком «Через час».
    /// </remarks>
    public int ReminderLeadMinutes { get; init; } = DEFAULT_REMINDER_LEAD_MINUTES;

    /// <summary>
    /// Бюджет попыток доставки; <c>Push:MaxAttempts</c>, по умолчанию 5.
    /// </summary>
    /// <remarks>
    /// Доменный предел — <c>Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS</c> (5), и большее
    /// значение конфигурации ограничивается им: шестая попытка не пролезла бы ни в check
    /// <c>delivery_attempts &lt;= 5</c>, ни в выборку пачки.
    /// </remarks>
    public int MaxAttempts { get; init; } = DEFAULT_MAX_ATTEMPTS;

    /// <summary>
    /// Ретенция строк push-очереди в днях; <c>Push:RetentionDays</c>, по умолчанию 30.
    /// </summary>
    public int RetentionDays { get; init; } = DEFAULT_RETENTION_DAYS;

    /// <summary>
    /// Ретенция токенов устройств в днях; <c>Push:DeviceTokenRetentionDays</c>, по умолчанию 180.
    /// </summary>
    public int DeviceTokenRetentionDays { get; init; } = DEFAULT_DEVICE_TOKEN_RETENTION_DAYS;

    /// <summary>
    /// Идентификатор проекта Firebase; <c>Push:ProjectId</c>. Пустой — канал не настроен.
    /// </summary>
    public string? ProjectId { get; init; }

    /// <summary>
    /// JSON сервисного аккаунта целиком; <c>Push:ServiceAccountJson</c>. Секрет: только
    /// user secrets или переменные окружения, никогда не <c>appsettings.json</c>.
    /// </summary>
    public string? ServiceAccountJson { get; init; }

    /// <summary>
    /// Путь к JSON сервисного аккаунта; <c>Push:ServiceAccountPath</c>. Альтернатива
    /// <see cref="ServiceAccountJson"/> для стендов, где секрет монтируется файлом.
    /// </summary>
    public string? ServiceAccountPath { get; init; }

    /// <summary>
    /// Предел активных устройств на пользователя; <c>Push:MaxTokensPerUser</c>, по умолчанию 5.
    /// </summary>
    /// <remarks>
    /// При превышении удаляется устройство с самым старым <c>last_seen_at</c>: телефон,
    /// который давно не заходил, скорее всего уже не принадлежит этому аккаунту.
    /// </remarks>
    public int MaxTokensPerUser { get; init; } = DEFAULT_MAX_TOKENS_PER_USER;

    /// <summary>
    /// Разобранный сервисный ключ либо <c>null</c>, если ключа нет или он битый.
    /// </summary>
    public ServiceAccountKey? ServiceAccount => _serviceAccountKey.Value;

    /// <summary>
    /// Настроен ли канал: есть идентификатор проекта и разбираемый сервисный ключ.
    /// </summary>
    /// <remarks>
    /// Секрет не проверяется на «правильность» — только на пригодность к подписи. Ошибка
    /// подписи или <c>401/403</c> от Google — временная ошибка доставки, а не падение старта.
    /// </remarks>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ProjectId) && ServiceAccount is not null;

    /// <summary>
    /// Печатает только несекретные флаги: ключ и его адрес в журнал не попадают.
    /// </summary>
    public override string ToString()
        => $"PushOptions {{ Enabled = {Enabled}, IsConfigured = {IsConfigured} }}";

    /// <summary>
    /// Читает ключ из строки конфигурации или из файла.
    /// </summary>
    /// <remarks>
    /// Файл читается один раз внутри <see cref="Lazy{T}"/>. Недоступный файл — «ключа нет»,
    /// а не исключение: приложение обязано собираться и работать без credentials.
    /// </remarks>
    private ServiceAccountKey? LoadServiceAccountKey()
    {
        if (!string.IsNullOrWhiteSpace(ServiceAccountJson))
        {
            return ServiceAccountKey.TryParse(ServiceAccountJson, out var inline) ? inline : null;
        }

        if (string.IsNullOrWhiteSpace(ServiceAccountPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(ServiceAccountPath);

            return ServiceAccountKey.TryParse(json, out var fromFile) ? fromFile : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
