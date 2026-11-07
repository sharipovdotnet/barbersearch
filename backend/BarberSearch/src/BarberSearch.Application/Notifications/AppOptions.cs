namespace BarberSearch.Application.Notifications;

/// <summary>
/// Настройки публичного адреса приложения — секция конфигурации <c>App</c>.
/// </summary>
/// <remarks>
/// <see cref="PublicUrl"/> нужен ссылкам в уведомлениях: <c>{PublicUrl}/barber/notifications</c>
/// и <c>{PublicUrl}</c> в тексте Telegram. Абсолютный адрес обязан приходить из конфигурации:
/// за обратным прокси <c>HttpContext.Request</c> может не знать внешнюю схему и хост.
/// </remarks>
public sealed record AppOptions
{
    /// <summary>
    /// Имя секции конфигурации: <c>App</c>.
    /// </summary>
    public const string SECTION = "App";

    /// <summary>
    /// Публичный корень приложения без завершающей косой черты; <c>App:PublicUrl</c>.
    /// </summary>
    public string? PublicUrl { get; init; }
}
