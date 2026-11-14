using Microsoft.Extensions.DependencyInjection;

namespace BarberSearch.Mobile.Navigation;

/// <summary>
/// Фабрика страниц Shell: страница резолвится из контейнера зависимостей.
/// </summary>
/// <remarks>
/// Без своей фабрики Shell создаёт зарегистрированную страницу рефлексией через пустой
/// конструктор, и страница с внедрённым ViewModel не появится. Тип страницы передаётся
/// параметром типа, а зависимости берёт DI — конструктор остаётся обычным.
/// </remarks>
/// <typeparam name="TPage">Страница, зарегистрированная в контейнере.</typeparam>
public sealed class PageRouteFactory<TPage> : RouteFactory
    where TPage : Page
{
    private readonly IServiceProvider _services;

    /// <summary>
    /// Создаёт фабрику над контейнером приложения.
    /// </summary>
    /// <param name="services">Контейнер, из которого резолвится страница.</param>
    public PageRouteFactory(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
    }

    /// <summary>
    /// Возвращает новый экземпляр страницы из контейнера.
    /// </summary>
    /// <returns>Страница Shell.</returns>
    public override Element GetOrCreate() => _services.GetRequiredService<TPage>();

    /// <summary>
    /// Возвращает новый экземпляр страницы из контейнера, который передал Shell.
    /// </summary>
    /// <param name="services">Контейнер, из которого резолвится страница.</param>
    /// <returns>Страница Shell.</returns>
    public override Element GetOrCreate(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.GetRequiredService<TPage>();
    }
}
