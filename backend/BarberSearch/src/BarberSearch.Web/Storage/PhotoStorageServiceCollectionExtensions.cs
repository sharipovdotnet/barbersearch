using BarberSearch.Application.PortfolioPhotos;

namespace BarberSearch.Web.Storage;

/// <summary>
/// Регистрация локального хранилища фотографий портфолио.
/// </summary>
/// <remarks>
/// Хранилище — host-адаптер слоя Web, поэтому регистрируется здесь, а не в Application.
/// Настройки разрешаются и каталог проверяется в момент регистрации: ошибка пути должна
/// ронять старт, а не первую загрузку.
/// </remarks>
public static class PhotoStorageServiceCollectionExtensions
{
    /// <summary>
    /// Добавляет <see cref="PhotoStorageOptions"/> и <see cref="IPhotoStorage"/> синглтонами.
    /// </summary>
    /// <param name="services">Коллекция сервисов приложения.</param>
    /// <param name="configuration">Конфигурация приложения (секция <c>Storage</c>).</param>
    /// <param name="environment">Окружение: относительно его content root разрешается путь.</param>
    /// <returns>Та же коллекция сервисов — для цепочки вызовов.</returns>
    /// <exception cref="InvalidOperationException">
    /// <c>Storage:RootPath</c> не задан или каталог нельзя создать либо записать в него.
    /// </exception>
    public static IServiceCollection AddPhotoStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = PhotoStorageOptions.FromConfiguration(configuration, environment);

        // Настройки неизменяемы после создания: один каталог на процесс.
        services.AddSingleton(options);

        // Реализация без состояния, файлы открываются на каждый запрос — singleton.
        services.AddSingleton<IPhotoStorage, LocalPhotoStorage>();

        return services;
    }
}
