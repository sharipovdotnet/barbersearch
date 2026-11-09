using BarberSearch.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace BarberSearch.Infrastructure.Email;

/// <summary>
/// Регистрация почтового транспорта в композиционном корне.
/// </summary>
/// <remarks>
/// Настройки приходят уже связанными: Web читает секцию <c>Email</c> так же, как
/// <c>Reminders</c>, <c>Telegram</c> и <c>App</c>. Слой Application этот тип не видит —
/// он знает только <see cref="IEmailSender"/>.
/// </remarks>
public static class EmailSenderExtensions
{
    /// <summary>
    /// Регистрирует настройки почты и SMTP-отправитель синглтонами.
    /// </summary>
    /// <param name="services">Коллекция сервисов композиционного корня.</param>
    /// <param name="options">Связанная секция <c>Email</c>; пустая секция допустима.</param>
    public static IServiceCollection AddEmailSender(
        this IServiceCollection services,
        EmailOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        return services;
    }
}
