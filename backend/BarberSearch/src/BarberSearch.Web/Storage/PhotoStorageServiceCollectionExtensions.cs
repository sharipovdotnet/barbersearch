using Amazon.Runtime;
using Amazon.S3;
using BarberSearch.Application.PortfolioPhotos;

namespace BarberSearch.Web.Storage;

/// <summary>
/// Регистрация хранилища изображений: фотографий портфолио и аватаров.
/// </summary>
/// <remarks>
/// Хранилище — host-адаптер слоя Web, поэтому регистрируется здесь, а не в Application.
/// Настройки разрешаются в момент регистрации: ошибка конфигурации должна ронять старт, а не
/// первую загрузку. Провайдер выбирается <c>Storage:Provider</c>: <c>Local</c> (по умолчанию)
/// пишет в каталог, <c>S3</c> — в бакет; оба реализуют один <see cref="IPhotoStorage"/>.
/// </remarks>
public static class PhotoStorageServiceCollectionExtensions
{
    /// <summary>
    /// Добавляет <see cref="PhotoStorageOptions"/>, <see cref="IPhotoThumbnailer"/> и
    /// <see cref="IPhotoStorage"/> синглтонами, а для S3 — клиент, шов и проверку бакета.
    /// </summary>
    /// <remarks>
    /// Генератор вариантов — синглтон, потому что его семафор ограничивает декодирование
    /// в масштабе процесса, а не запроса: два одновременных кадра — предел памяти. Проверка
    /// бакета — <see cref="S3StorageInitializer"/>, а не код регистрации: она асинхронна и
    /// требует сети.
    /// </remarks>
    /// <param name="services">Коллекция сервисов приложения.</param>
    /// <param name="configuration">Конфигурация приложения (секция <c>Storage</c>).</param>
    /// <param name="environment">Окружение: относительно его content root разрешается путь.</param>
    /// <returns>Та же коллекция сервисов — для цепочки вызовов.</returns>
    /// <exception cref="InvalidOperationException">
    /// Провайдер неизвестен, обязательный ключ не задан, путь некорректен либо каталог нельзя
    /// создать или записать в него.
    /// </exception>
    public static IServiceCollection AddPhotoStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = PhotoStorageOptions.FromConfiguration(configuration, environment);

        // Настройки неизменяемы после создания: один провайдер на процесс.
        services.AddSingleton(options);

        // Генератор вариантов без состояния, с общим семафором на процесс.
        services.AddSingleton<IPhotoThumbnailer, PhotoThumbnailer>();

        switch (options.Provider)
        {
            case PhotoStorageProvider.Local:
                // Реализация без состояния, файлы открываются на каждый запрос — singleton.
                services.AddSingleton<IPhotoStorage, LocalPhotoStorage>();

                break;

            case PhotoStorageProvider.S3:
                services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
                    new BasicAWSCredentials(options.S3.AccessKey, options.S3.SecretKey),
                    new AmazonS3Config
                    {
                        // MinIO живёт по конкретному адресу и ждёт бакет в пути, а не в поддомене.
                        ServiceURL = options.S3.ServiceUrl,
                        ForcePathStyle = options.S3.ForcePathStyle,

                        // MinIO требует подпись с регионом; AWS S3 — свой регион бакета.
                        AuthenticationRegion = options.S3.Region
                    }));

                services.AddSingleton<IPhotoObjectStore, S3ObjectStore>();
                services.AddSingleton<IPhotoStorage, S3PhotoStorage>();

                // Проверка (и при EnsureBucket — создание) бакета: ошибка роняет старт.
                services.AddHostedService<S3StorageInitializer>();

                break;

            default:
                // Защита от будущего значения enum: провайдер без реализации не должен
                // молча уходить в локальный диск.
                throw new InvalidOperationException(
                    $"Storage:Provider '{options.Provider}' has no registered photo storage. "
                    + $"Allowed values: '{nameof(PhotoStorageProvider.Local)}' and "
                    + $"'{nameof(PhotoStorageProvider.S3)}'.");
        }

        return services;
    }
}
