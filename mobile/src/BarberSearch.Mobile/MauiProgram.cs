using BarberSearch.Mobile.Auth;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;
using BarberSearch.Mobile.Core.Api.Auth;
using BarberSearch.Mobile.Core.Api.Bookings;
using BarberSearch.Mobile.Core.Api.Reviews;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Legal;
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
    /// Адрес публичного сайта: правовые документы открываются в браузере, а не в приложении.
    /// </summary>
    /// <remarks>
    /// Совпадает с <c>App:PublicUrl</c> бэкенда в разработке — сайт запускается на 5173.
    /// Эмулятор Android видит хост как <c>10.0.2.2</c>, симулятор iOS — как <c>localhost</c>.
    /// Адрес стенда появится здесь же, когда он будет.
    /// </remarks>
    private const string DevelopmentSiteBaseAddress =
#if ANDROID
        "http://10.0.2.2:5173";
#else
        "http://localhost:5173";
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
            // Geist — тот же шрифт, что в вебе (frontend/src/index.css: --font-sans). MAUI не грузит
            // WOFF2, поэтому здесь TTF из релиза 1.9.2, а не файлы из frontend/node_modules.
            // Веса — отдельными файлами: ось wght вариативного шрифта на Android ведёт себя
            // по-разному в зависимости от версии системы, а GeistSemiBold нужен заголовкам.
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("Geist-Regular.ttf", "Geist");
                fonts.AddFont("Geist-Medium.ttf", "GeistMedium");
                fonts.AddFont("Geist-SemiBold.ttf", "GeistSemiBold");
            });

        builder.Services.AddSingleton(BarberSearchApiOptions.FromBaseAddress(DevelopmentApiBaseAddress));
        builder.Services.AddSingleton(LegalOptions.FromBaseAddress(DevelopmentSiteBaseAddress));

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

        // Бронь — второе чтение/запись с Bearer; публичные карточка и слоты остаются анонимными.
        builder.Services.AddHttpClient<IBookingsApi, BookingsApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();

        // Отзыв — третья запись с Bearer: автора сервер берёт из токена, барбера — из брони.
        builder.Services.AddHttpClient<IReviewsApi, ReviewsApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();

        builder.Services.AddHttpClient<IBarberSearchApi, BarberSearchApi>(ConfigureHttpClient);

        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddTransient<SearchViewModel>();
        builder.Services.AddTransient<ProfileViewModel>();
        builder.Services.AddTransient<RegisterViewModel>();
        builder.Services.AddTransient<BarberCardViewModel>();
        builder.Services.AddTransient<PortfolioViewerViewModel>();

        // Экран «Записи» — singleton, поэтому его ViewModel создаётся ровно один раз и живёт
        // столько же: состояние вкладки переживает переключения между разделами.
        builder.Services.AddTransient<BookingsViewModel>();

        // Вспомогательные экраны открываются поверх стека и живут один визит.
        builder.Services.AddTransient<RescheduleBookingViewModel>();
        builder.Services.AddTransient<ReviewBookingViewModel>();

        // Разделы кабинета тоже живут один визит: каждый читает профиль при появлении
        // и не должен хранить данные прошлого пользователя между открытиями.
        builder.Services.AddTransient<AccountProfileViewModel>();
        builder.Services.AddTransient<SecurityViewModel>();
        builder.Services.AddTransient<MyReviewsViewModel>();
        builder.Services.AddTransient<EditReviewViewModel>();

        builder.Services.AddTransient<SearchPage>();
        builder.Services.AddTransient<BarberPage>();
        builder.Services.AddTransient<PortfolioPhotoPage>();
        builder.Services.AddTransient<RescheduleBookingPage>();
        builder.Services.AddTransient<ReviewBookingPage>();
        builder.Services.AddTransient<AccountProfilePage>();
        builder.Services.AddTransient<SecurityPage>();
        builder.Services.AddTransient<MyReviewsPage>();
        builder.Services.AddTransient<EditReviewPage>();
        builder.Services.AddTransient<RegisterPage>();
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
