using BarberSearch.Mobile.Auth;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;
using BarberSearch.Mobile.Core.Api.Auth;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels;
using BarberSearch.Mobile.Pages;
using Microsoft.Extensions.Logging;

namespace BarberSearch.Mobile;

/// <summary>
/// Композиционный корень приложения: зависимости экранов и адрес API.
/// </summary>
public static class MauiProgram
{
    /// <summary>
    /// Адрес локального бэкенда.
    /// </summary>
    /// <remarks>
    /// Эмулятор Android видит хост разработчика как <c>10.0.2.2</c>, симулятор iOS — как
    /// <c>localhost</c>. Адрес стенда появится здесь же, когда он будет.
    /// </remarks>
    private const string DevelopmentApiBaseAddress =
#if ANDROID
        "http://10.0.2.2:5243";
#else
        "http://localhost:5243";
#endif

    /// <summary>
    /// Создаёт и настраивает MAUI-приложение.
    /// </summary>
    /// <returns>Готовое приложение.</returns>
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton(BarberSearchApiOptions.FromBaseAddress(DevelopmentApiBaseAddress));

        // Пара токенов живёт в защищённом хранилище платформы; Core про него не знает.
        builder.Services.AddSingleton<ITokenStore, SecureStorageTokenStore>();
        builder.Services.AddSingleton<TokenSession>();

        // Вход, refresh и выход идут на клиент без обработчика: иначе refresh, получивший 401,
        // вызывал бы сам себя, а выход отправлял бы уже отозванный access-токен.
        builder.Services.AddHttpClient<IAuthApi, AuthApi>(ConfigureHttpClient);

        // Обработчик носит Bearer и продлевает пару; клиентов с ним может быть несколько,
        // но refresh в нём один — через семафор внутри экземпляра.
        builder.Services.AddTransient<AuthenticatedHttpHandler>();

        builder.Services.AddHttpClient<IAccountApi, AccountApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();

        builder.Services.AddHttpClient<IBarberSearchApi, BarberSearchApi>(ConfigureHttpClient);

        builder.Services.AddTransient<SearchViewModel>();
        builder.Services.AddTransient<ProfileViewModel>();

        builder.Services.AddTransient<SearchPage>();
        builder.Services.AddSingleton<MapPage>();
        builder.Services.AddSingleton<BookingsPage>();
        builder.Services.AddSingleton<ProfilePage>();

        builder.Services.AddSingleton<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    /// <summary>
    /// Общие настройки любого клиента API: адрес, таймаут и никаких логов с телами запросов.
    /// </summary>
    private static void ConfigureHttpClient(IServiceProvider services, HttpClient httpClient)
    {
        httpClient.BaseAddress = services.GetRequiredService<BarberSearchApiOptions>().BaseAddress;
        httpClient.Timeout = TimeSpan.FromSeconds(30);
    }
}
