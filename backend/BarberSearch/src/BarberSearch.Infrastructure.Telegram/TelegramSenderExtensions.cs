using BarberSearch.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace BarberSearch.Infrastructure.Telegram;

/// <summary>
/// Регистрация Telegram-канала в композиционном корне.
/// </summary>
/// <remarks>
/// Настройки приходят уже связанными: Web читает секцию <c>Telegram</c> так же, как
/// <c>Email</c>, <c>Reminders</c> и <c>App</c>. Слой Application этот тип не видит —
/// он знает только <see cref="ITelegramBot"/> и <see cref="ITelegramSender"/>.
/// </remarks>
public static class TelegramSenderExtensions
{
    /// <summary>
    /// Регистрирует настройки бота, публичный порт <see cref="ITelegramBot"/> и
    /// HTTP-отправитель <see cref="ITelegramSender"/> синглтонами.
    /// </summary>
    /// <param name="services">Коллекция сервисов композиционного корня.</param>
    /// <param name="options">Связанная секция <c>Telegram</c>; пустая секция допустима.</param>
    public static IServiceCollection AddTelegramSender(
        this IServiceCollection services,
        TelegramOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<ITelegramBot>(options);

        // Отдельного NuGet-пакета для Bot API нет: отправка — обычный POST формы через
        // именованный HttpClient с таймаутом одного вызова.
        services.AddHttpClient(
            TelegramSender.HTTP_CLIENT_NAME,
            client => client.Timeout = TelegramSender.REQUEST_TIMEOUT);

        services.AddSingleton<ITelegramSender, TelegramSender>();

        return services;
    }
}
