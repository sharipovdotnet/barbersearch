using BarberSearch.Application.Push;
using Microsoft.Extensions.DependencyInjection;

namespace BarberSearch.Infrastructure.Push;

/// <summary>
/// Регистрация push-канала в композиционном корне.
/// </summary>
/// <remarks>
/// Настройки приходят уже связанными и проверенными: Web читает секцию <c>Push</c> через
/// <c>StartupSettings</c>, как <c>EmailOutbox</c>. Слой Application этот тип не видит — он
/// знает только <see cref="IPushSender"/>. Отдельного NuGet-пакета для FCM нет: отправка —
/// обычный POST через именованный <c>HttpClient</c> с таймаутом одного вызова.
/// </remarks>
public static class PushSenderExtensions
{
    /// <summary>
    /// Регистрирует настройки, провайдер OAuth-токена и HTTP-отправитель синглтонами.
    /// </summary>
    /// <param name="services">Коллекция сервисов композиционного корня.</param>
    /// <param name="options">Связанная и проверенная секция <c>Push</c>; пустые ключи допустимы.</param>
    public static IServiceCollection AddPushSender(
        this IServiceCollection services,
        PushOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);

        services.AddHttpClient(
            FcmPushSender.HTTP_CLIENT_NAME,
            client => client.Timeout = FcmPushSender.REQUEST_TIMEOUT);

        services.AddSingleton<FcmAccessTokenProvider>();
        services.AddSingleton<IPushSender, FcmPushSender>();

        return services;
    }
}
