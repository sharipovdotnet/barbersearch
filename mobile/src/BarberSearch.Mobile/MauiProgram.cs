using BarberSearch.Mobile.Core.Api;
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

        builder.Services.AddHttpClient<IBarberSearchApi, BarberSearchApi>((services, httpClient) =>
        {
            httpClient.BaseAddress = services.GetRequiredService<BarberSearchApiOptions>().BaseAddress;
            httpClient.Timeout = TimeSpan.FromSeconds(30);
        });

        builder.Services.AddTransient<SearchViewModel>();

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
}
