using Microsoft.AspNetCore.DataProtection;

namespace BarberSearch.Web.Deployment;

/// <summary>
/// Регистрация Data Protection с постоянным key ring.
/// </summary>
public static class DataProtectionExtensions
{
    /// <summary>
    /// Подключает Data Protection и кладёт ключи в каталог из конфигурации.
    /// </summary>
    /// <remarks>
    /// <c>SetApplicationName</c> обязателен: без него приложение выводит имя из content root,
    /// и два инстанса, запущенные из разных каталогов, не смогут прочитать cookie друг друга
    /// даже при общем томе. Если настроен сертификат, ключи на диске шифруются им.
    /// </remarks>
    public static IServiceCollection AddPersistentDataProtection(
        this IServiceCollection services,
        DataProtectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);

        var dataProtection = services
            .AddDataProtection()
            .SetApplicationName(DataProtectionSettings.ApplicationName)
            .PersistKeysToFileSystem(settings.GetKeysDirectory());

        var certificate = settings.LoadCertificate();

        if (certificate is not null)
        {
            dataProtection.ProtectKeysWithCertificate(certificate);
        }

        return services;
    }
}
