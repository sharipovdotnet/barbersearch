using System.Reflection;
using BarberSearch.Mobile.Auth;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;
using BarberSearch.Mobile.Core.Api.Auth;
using BarberSearch.Mobile.Core.Api.Bookings;
using BarberSearch.Mobile.Core.Api.Reviews;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Configuration;
using BarberSearch.Mobile.Core.Legal;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.Theming;
using BarberSearch.Mobile.Core.ViewModels;
using BarberSearch.Mobile.Localization;
using BarberSearch.Mobile.Pages;
using BarberSearch.Mobile.Theming;
using Microsoft.Extensions.Logging;

namespace BarberSearch.Mobile;

/// <summary>
/// Композиционный корень приложения: зависимости экранов и адрес API.
/// </summary>
public static class MauiProgram
{
    /// <summary>
    /// Ключ метаданных сборки, в которые .csproj пишет хост из <c>-p:DevApiHost</c>.
    /// </summary>
    private const string DevelopmentHostMetadataKey = "BarberSearch.DevApiHost";

    /// <summary>
    /// Хост локального бэкенда, заданный при сборке, или <c>null</c>, если он не задан.
    /// </summary>
    /// <remarks>
    /// Пример: <c>dotnet build -f net10.0-android -p:DevApiHost=192.168.1.10</c> — телефон в той же
    /// сети, без проброса портов. Значение читается только в отладочной сборке: в релизе такого
    /// параметра нет, а рефлексия в урезанной сборке — лишний риск. Без параметра хост выбирается
    /// по устройству, см. <see cref="DevelopmentEndpoints"/>.
    /// </remarks>
    private static string? ConfiguredDevelopmentHost
    {
        get
        {
#if DEBUG
            return typeof(MauiProgram).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == DevelopmentHostMetadataKey)?.Value;
#else
            return null;
#endif
        }
    }

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

        // Адрес бэкенда и сайта — по устройству и сборке: эмулятор видит машину по 10.0.2.2,
        // реальный телефон — по localhost через `adb reverse`, а -p:DevApiHost задаёт хост явно.
        var developmentHost = ResolveDevelopmentHost();
        var apiBaseAddress = DevelopmentEndpoints.ApiBaseAddress(developmentHost);
        var siteBaseAddress = DevelopmentEndpoints.SiteBaseAddress(developmentHost);

        builder.Services.AddSingleton(BarberSearchApiOptions.FromBaseAddress(apiBaseAddress));
        builder.Services.AddSingleton(LegalOptions.FromBaseAddress(siteBaseAddress));

        // Язык интерфейса читается один раз на старте: XAML-разметка и статические форматтеры
        // берут его из модульного Loc, а приложение может его сменить из «Профиля».
        var localeStore = new PreferencesLocaleStore();
        var localization = new BarberSearch.Mobile.Core.Localization.Localization(localeStore);

        builder.Services.AddSingleton<ILocaleStore>(localeStore);
        builder.Services.AddSingleton<ILocalization>(localization);

        // Тема — настройка устройства, поэтому хранится рядом с языком, но отдельно:
        // выбор переживает перезапуск, а по умолчанию приложение следует теме системы.
        builder.Services.AddSingleton<IThemeStore, PreferencesThemeStore>();
        builder.Services.AddSingleton<ThemeService>();

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
        builder.Services.AddTransient<ForgotPasswordViewModel>();
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
        builder.Services.AddTransient<ForgotPasswordPage>();

        // Вкладки тоже transient: смена языка пересобирает Shell, и каждая страница должна
        // получить ViewModel с новым языком. Состояние вкладки сохраняется между визитами,
        // пока Shell один; после смены языка экран начинается заново, как после перезагрузки
        // страницы в вебе.
        builder.Services.AddTransient<MapPage>();
        builder.Services.AddTransient<BookingsPage>();
        builder.Services.AddTransient<ProfilePage>();

        builder.Services.AddTransient<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    /// <summary>
    /// Выбирает хост локального бэкенда для устройства, на котором запущено приложение.
    /// </summary>
    /// <remarks>
    /// Эмулятор Android видит машину разработчика как <c>10.0.2.2</c>, а реальный телефон — нет:
    /// на телефоне этот адрес указывает на сам телефон. Телефон ходит на <c>localhost</c>, который
    /// на компьютер пробрасывает <c>adb reverse tcp:5243 tcp:5243</c> (и <c>tcp:5173</c> для
    /// сайта), поэтому адрес не зависит от IP-адреса машины и её брандмауэра, а меняется одной
    /// командой на подключение. Заданный при сборке хост важнее правила — см.
    /// <see cref="DevelopmentEndpoints"/>.
    /// </remarks>
    private static string ResolveDevelopmentHost() => DevelopmentEndpoints.ResolveHost(
        ConfiguredDevelopmentHost,
        androidPlatform: DeviceInfo.Current.Platform == DevicePlatform.Android,
        virtualDevice: DeviceInfo.Current.DeviceType == DeviceType.Virtual);

    /// <summary>
    /// Общие настройки любого клиента API: адрес, таймаут и никаких логов с телами запросов.
    /// </summary>
    private static void ConfigureHttpClient(IServiceProvider services, HttpClient httpClient)
    {
        httpClient.BaseAddress = services.GetRequiredService<BarberSearchApiOptions>().BaseAddress;
        httpClient.Timeout = TimeSpan.FromSeconds(30);
    }
}
