using System.Net.Mail;

namespace BarberSearch.Application.EmailVerifications;

/// <summary>
/// Настройки отправки писем — секция конфигурации <c>Email</c>.
/// </summary>
/// <remarks>
/// <para>
/// Заполняется композиционным корнем Web и регистрируется синглтоном, как
/// <c>TelegramOptions</c>. Пароль — секрет: он приходит из user secrets или переменных
/// окружения и никогда не попадает в ответы API и в логи.
/// </para>
/// <para>
/// Пустая секция — нормальное состояние: приложение работает, регистрация отвечает
/// <c>201</c>, письмо не уходит, а <c>request</c> возвращает
/// <c>409 auth.emailVerification.unavailable</c>.
/// </para>
/// <para>
/// <see cref="UseTls"/> — это STARTTLS: <see cref="SmtpClient"/> не умеет implicit TLS
/// на порту 465. Для локальной разработки Mailpit принимает письма без TLS на порту 1025.
/// </para>
/// </remarks>
public sealed record EmailOptions
{
    /// <summary>
    /// Имя секции конфигурации: <c>Email</c>.
    /// </summary>
    public const string SECTION = "Email";

    /// <summary>
    /// Таймаут по умолчанию: регистрация не должна ждать почту дольше.
    /// </summary>
    public const int DEFAULT_TIMEOUT_SECONDS = 5;

    /// <summary>
    /// Порт по умолчанию: 587/STARTTLS.
    /// </summary>
    public const int DEFAULT_PORT = 587;

    /// <summary>
    /// Включена ли отправка писем; <c>Email:Enabled</c>.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Хост SMTP-сервера; <c>Email:Host</c>. Пустой — почта не настроена.
    /// </summary>
    public string? Host { get; init; }

    /// <summary>
    /// Порт SMTP-сервера; <c>Email:Port</c>. Должен быть больше нуля.
    /// </summary>
    public int Port { get; init; } = DEFAULT_PORT;

    /// <summary>
    /// Использовать ли STARTTLS; <c>Email:UseTls</c>.
    /// </summary>
    public bool UseTls { get; init; } = true;

    /// <summary>
    /// Имя пользователя SMTP; <c>Email:UserName</c>. Пустое — сервер без аутентификации.
    /// </summary>
    public string? UserName { get; init; }

    /// <summary>
    /// Пароль SMTP; <c>Email:Password</c>. Секрет: в <c>appsettings.json</c> остаётся пустым.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Адрес отправителя; <c>Email:FromAddress</c>. Должен быть валидным адресом почты.
    /// </summary>
    public string? FromAddress { get; init; }

    /// <summary>
    /// Отображаемое имя отправителя; <c>Email:FromName</c>.
    /// </summary>
    public string FromName { get; init; } = "BarberSearch";

    /// <summary>
    /// Таймаут одного SMTP-вызова в секундах; <c>Email:TimeoutSeconds</c>.
    /// </summary>
    /// <remarks>
    /// 5 секунд, а не 100 по умолчанию у <see cref="SmtpClient"/>: таймаут письма — это
    /// «письмо не ушло», пользователь отправит его ещё раз из кабинета.
    /// </remarks>
    public int TimeoutSeconds { get; init; } = DEFAULT_TIMEOUT_SECONDS;

    /// <summary>
    /// Настроена ли отправка: включена, есть хост и порт, а <see cref="FromAddress"/> —
    /// валидный адрес. Адрес <c>App:PublicUrl</c> проверяется отдельно в обработчике.
    /// </summary>
    public bool IsConfigured
        => Enabled
           && !string.IsNullOrWhiteSpace(Host)
           && Port > 0
           && !string.IsNullOrWhiteSpace(FromAddress)
           && MailAddress.TryCreate(FromAddress, out _);
}
