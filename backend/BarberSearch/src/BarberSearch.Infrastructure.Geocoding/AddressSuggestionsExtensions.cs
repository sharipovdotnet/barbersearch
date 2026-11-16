using BarberSearch.Application.Addresses;
using Microsoft.Extensions.DependencyInjection;

namespace BarberSearch.Infrastructure.Geocoding;

/// <summary>
/// Регистрация подсказок адреса в композиционном корне.
/// </summary>
/// <remarks>
/// <para>
/// Настройки приходят уже связанными и проверенными: Web читает секцию
/// <c>AddressSuggestions</c> через <c>StartupSettings</c>, как <c>Push</c> и <c>Telegram</c>.
/// Слой Application этот тип не видит — он знает только
/// <see cref="IAddressSuggestionsProvider"/>.
/// </para>
/// <para>
/// Цепочка — порт → кэш → адаптер DaData. Кэш стоит перед адаптером, поэтому повторный
/// запрос той же строки не расходует платную квоту вендора. Адаптер регистрируется ещё и
/// сам по себе, чтобы декоратор получил его напрямую: если бы кэш забирал
/// <see cref="IAddressSuggestionsProvider"/> из контейнера, он обернул бы сам себя.
/// </para>
/// </remarks>
public static class AddressSuggestionsExtensions
{
    /// <summary>
    /// Регистрирует настройки секции, именованный <see cref="HttpClient"/> с таймаутом,
    /// адаптер DaData и кэширующий декоратор порта синглтонами.
    /// </summary>
    /// <param name="services">Коллекция сервисов композиционного корня.</param>
    /// <param name="options">
    /// Связанная и проверенная секция <c>AddressSuggestions</c>; выключенная фича и пустой
    /// ключ допустимы — старт не падает, эндпоинты отвечают <c>409</c>.
    /// </param>
    public static IServiceCollection AddAddressSuggestions(
        this IServiceCollection services,
        AddressSuggestionsOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);

        // Таймаут одного вызова задаётся настройкой, без ретраев: подсказка, пришедшая
        // через пять секунд, хуже её отсутствия.
        services.AddHttpClient(
            DadataAddressSuggestionsProvider.HTTP_CLIENT_NAME,
            client => client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds));

        services.AddSingleton<DadataAddressSuggestionsProvider>();

        // TimeProvider приходит из DI: в тестах это управляемые часы, и TTL кэша
        // проверяется без ожидания.
        services.AddSingleton<IAddressSuggestionsProvider>(sp =>
            new CachedAddressSuggestionsProvider(
                sp.GetRequiredService<DadataAddressSuggestionsProvider>(),
                options,
                sp.GetRequiredService<TimeProvider>()));

        return services;
    }
}
