using BarberSearch.Domain;

namespace BarberSearch.Application.Notifications;

/// <summary>
/// Настройки фонового воркера напоминаний — секция конфигурации <c>Reminders</c>.
/// </summary>
/// <remarks>
/// <para>
/// Заполняется композиционным корнем Web:
/// <c>configuration.GetSection(RemindersOptions.SECTION).Get&lt;RemindersOptions&gt;()</c>,
/// после чего регистрируется синглтоном. Application читает только значения — ни
/// <c>IConfiguration</c>, ни <c>IOptions</c> в слой не проникают.
/// </para>
/// <para>
/// <see cref="Enabled"/> по умолчанию <c>false</c> (решение D8): локальная разработка
/// не должна писать в Telegram и создавать строки напоминаний. В тестах флаг включается
/// конфигурацией, а не кодом.
/// </para>
/// </remarks>
public sealed record RemindersOptions
{
    /// <summary>
    /// Имя секции конфигурации: <c>Reminders</c>.
    /// </summary>
    public const string SECTION = "Reminders";

    /// <summary>
    /// Включён ли воркер. По умолчанию выключен: ни одного сообщения и ни одной строки в БД.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Размер пачки доставки за один тик; <c>Reminders:BatchSize</c>, по умолчанию 100.
    /// </summary>
    public int BatchSize { get; init; } = Constants.NOTIFICATION_BATCH_SIZE;

    /// <summary>
    /// Ретенция инбокса в днях; <c>Reminders:RetentionDays</c>, по умолчанию 180.
    /// </summary>
    public int RetentionDays { get; init; } = Constants.NOTIFICATION_RETENTION_DAYS;
}
