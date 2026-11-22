using System.Reflection;
using BarberSearch.Mobile.Auth;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;
using BarberSearch.Mobile.Core.Api.Addresses;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Api.Auth;
using BarberSearch.Mobile.Core.Api.Barber;
using BarberSearch.Mobile.Core.Api.BarberRequests;
using BarberSearch.Mobile.Core.Api.Bookings;
using BarberSearch.Mobile.Core.Api.Push;
using BarberSearch.Mobile.Core.Api.Reviews;
using BarberSearch.Mobile.Core.Api.Salons;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Configuration;
using BarberSearch.Mobile.Core.Legal;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.Location;
using BarberSearch.Mobile.Core.Media;
using BarberSearch.Mobile.Core.Push;
using BarberSearch.Mobile.Core.Theming;
using BarberSearch.Mobile.Core.ViewModels;
using BarberSearch.Mobile.Core.ViewModels.Admin;
using BarberSearch.Mobile.Core.ViewModels.Barber;
using BarberSearch.Mobile.Devices;
using BarberSearch.Mobile.Localization;
using BarberSearch.Mobile.Navigation;
using BarberSearch.Mobile.Pages;
using BarberSearch.Mobile.Push;
using BarberSearch.Mobile.Services;
using BarberSearch.Mobile.Theming;
using BarberSearch.Mobile.Views.Barber;
using BarberBookingsSectionView = BarberSearch.Mobile.Views.Barber.BarberBookingsView;
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

#if ANDROID
        // Долгое нажатие на подпись вкладки показывает tooltip: он должен называть раздел
        // целиком, а не повторять короткую подпись, которая помещается в полосе.
        builder.ConfigureMauiHandlers(handlers =>
            handlers.AddHandler<AppShell, TabHintShellRenderer>());
#endif

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

        // Push настраивается один раз на старте: Android читает push.json и, если он есть,
        // инициализирует Firebase до первого обращения к токену. Без ресурса — Disabled:
        // ни разрешений, ни переключателя в «Профиле», приложение работает как раньше.
#if ANDROID
        var pushOptions = PushBootstrap.Initialize();
#else
        var pushOptions = PushOptions.Disabled;
#endif
        builder.Services.AddSingleton(pushOptions);
        builder.Services.AddSingleton<IPushStateStore, PreferencesPushStateStore>();
        builder.Services.AddSingleton<IPushRegistrar, PushRegistrar>();

        // Единственный переход по нажатию на напоминание: и холодный старт, и тёплый
        // складывают цель сюда, а Shell забирает её, когда уже существует.
        builder.Services.AddSingleton<NotificationTapRouter>();

#if ANDROID
        // Провайдер и разрешение — устройство, а не аккаунт: один экземпляр на приложение.
        // Конкретные типы зарегистрированы отдельно, чтобы служба FCM могла сообщить
        // обновлённый токен тому же экземпляру, который отдаёт его регистратору.
        builder.Services.AddSingleton<AndroidPushPermission>();
        builder.Services.AddSingleton<IPushPermission>(
            services => services.GetRequiredService<AndroidPushPermission>());
        builder.Services.AddSingleton<FcmPushTokenProvider>();
        builder.Services.AddSingleton<IPushTokenProvider>(
            services => services.GetRequiredService<FcmPushTokenProvider>());
#else
        // iOS: push появится отдельным срезом на APNs. Заглушки отвечают «выключено»,
        // поэтому ни Firebase, ни разрешений в iOS-сборке нет.
        builder.Services.AddSingleton<IPushPermission, IosPushPermission>();
        builder.Services.AddSingleton<IPushTokenProvider, IosPushTokenProvider>();
#endif

        // Вход, refresh и выход идут на клиент без обработчика: иначе refresh, получивший 401,
        // вызывал бы сам себя, а выход отправлял бы уже отозванный access-токен.
        builder.Services.AddHttpClient<IAuthApi, AuthApi>(ConfigureHttpClient);

        // Обработчик носит Bearer и продлевает пару; клиентов с ним может быть несколько,
        // но refresh в нём один — через семафор внутри экземпляра.
        builder.Services.AddTransient<AuthenticatedHttpHandler>();

        builder.Services.AddHttpClient<IAccountApi, AccountApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();

        // Аватар — тот же аккаунт с Bearer, но отдельная точка внедрения: кабинет зависит
        // от интерфейса аватара, а не от всего клиента аккаунта.
        builder.Services.AddHttpClient<IAccountAvatarApi, AccountApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();

        // Бронь — второе чтение/запись с Bearer; публичные карточка и слоты остаются анонимными.
        builder.Services.AddHttpClient<IBookingsApi, BookingsApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();

        // Отзыв — третья запись с Bearer: автора сервер берёт из токена, мастера — из брони.
        builder.Services.AddHttpClient<IReviewsApi, ReviewsApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();

        // Заявка на роль мастера — чтение и запись с Bearer: роль сервер берёт из токена,
        // а antiforgery-фильтр пропускает такой запрос без X-CSRF-TOKEN.
        builder.Services.AddHttpClient<IBarberRequestsApi, BarberRequestsApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();

        // Регистрация push-устройства — тоже с Bearer: пользователя сервер берёт из токена.
        builder.Services.AddHttpClient<IPushApi, PushApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();

        // Админская сводка — чтение с Bearer: роль администратора сервер берёт из токена,
        // и без неё ответ 403. Клиент один на все разделы администрирования.
        builder.Services.AddHttpClient<IAdminSummaryApi, AdminSummaryApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();

        // Очереди модерации — свои клиенты по разделам: у каждого свой набор чтений и решений,
        // а Bearer, роль и 403 у них общие. Сводка остаётся одна: числа считает сервер.
        builder.Services.AddHttpClient<IAdminReviewsApi, AdminReviewsApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();
        builder.Services.AddHttpClient<IAdminPhotosApi, AdminPhotosApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();
        builder.Services.AddHttpClient<IAdminMastersApi, AdminMastersApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();
        builder.Services.AddHttpClient<IAdminRequestsApi, AdminRequestsApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();

        // Превью модерации: раздача media отдаёт неодобренный кадр только администратору,
        // поэтому байты читает приложение, а не загрузчик картинок платформы без заголовков.
        builder.Services.AddHttpClient<IAdminMediaApi, AdminMediaApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();

        // Кабинет мастера — пять клиентов по разделам: записи и действия над ними, форма
        // записи клиента, расписание, карточка с прайсом и инбокс. Роль и профиль мастер
        // сервер берёт из токена, поэтому все пять идут с Bearer и без X-CSRF-TOKEN.
        builder.Services.AddHttpClient<IBarberBookingsApi, BarberBookingsApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();
        builder.Services.AddHttpClient<IBarberClientsApi, BarberClientsApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();
        builder.Services.AddHttpClient<IBarberScheduleApi, BarberScheduleApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();
        builder.Services.AddHttpClient<IBarberProfileApi, BarberProfileApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();
        builder.Services.AddHttpClient<IBarberNotificationsApi, BarberNotificationsApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();

        // Создание своего салона и подсказки адреса — тоже кабинет с Bearer: профиль барбера
        // сервер берёт из токена, поэтому X-CSRF-TOKEN не нужен, а создать место можно только
        // владельцу. Города подсказываются анонимным клиентом «Поиска» и остаются как есть.
        builder.Services.AddHttpClient<ISalonsApi, SalonsApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();
        builder.Services.AddHttpClient<IAddressesApi, AddressesApi>(ConfigureHttpClient)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();

        builder.Services.AddHttpClient<IBarberSearchApi, BarberSearchApi>(ConfigureHttpClient);

        builder.Services.AddSingleton(TimeProvider.System);

        // Цикл автоповтора чтения — свой у каждого transient-экрана: общий на приложение
        // повторял бы запрос одного экрана, пока другой уже ушёл. Регистрация явная, хотя
        // параметр конструктора необязательный: контейнер не должен полагаться на умолчание
        // параметра по умолчанию.
        builder.Services.AddTransient(static _ => new AutoRetryLoop());

        // Геопозиция — устройство, а не аккаунт: сервис живёт один на приложение и не хранит
        // состояние между экранами. Поиск зависит от интерфейса Core, поэтому проверяется
        // без эмулятора и без разрешений ОС.
        builder.Services.AddSingleton<ILocationService, MauiLocationService>();

        // Город по координатам — тот же слой устройства, что и геопозиция: синглтон без
        // состояния, за интерфейсом Core, чтобы экран проверялся без геокодера платформы.
        builder.Services.AddSingleton<ICityLocator, MauiCityLocator>();

        // Файл портфолио выбирает галерея устройства (FilePicker), а уменьшает и
        // перекодирует приложение: сервер принимает JPEG до 5 МБ. Синглтон без состояния.
        builder.Services.AddSingleton<IPortfolioPhotoPicker, MauiPortfolioPhotoPicker>();

        // Подсказки города — часть экрана «Поиск»: одна секция на один ViewModel, поэтому
        // transient рядом с ним, а не синглтон. Адреса — пара к форме создания салона: тоже
        // transient и отдельный класс, потому что живут на авторизованном клиенте.
        builder.Services.AddTransient<CitySuggestions>();
        builder.Services.AddTransient<AddressSuggestions>();
        builder.Services.AddTransient<SearchViewModel>();
        builder.Services.AddTransient<ProfileViewModel>();
        builder.Services.AddTransient<BarberRequestViewModel>();
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

        // Хаб администрирования — один визит: сводка перечитывается при каждом появлении,
        // а состояние прошлого захода хранить нечего.
        builder.Services.AddTransient<AdminHubViewModel>();

        // Разделы администрирования живут один визит: список и курсор не должны переживать
        // выход с экрана, а после решения очередь перечитывается при возврате.
        builder.Services.AddTransient<AdminReviewsViewModel>();
        builder.Services.AddTransient<AdminReviewViewModel>();
        builder.Services.AddTransient<AdminPhotosViewModel>();
        builder.Services.AddTransient<AdminMastersViewModel>();
        builder.Services.AddTransient<AdminMasterViewModel>();
        builder.Services.AddTransient<AdminRequestsViewModel>();
        builder.Services.AddTransient<AdminRequestViewModel>();

        // Кабинет мастера — один экран с четырьмя разделами: оболочка проверяет сессию,
        // роль и профиль, а разделы живут внутри страницы. Экран открывается из «Профиля»
        // и живёт один визит: состояние прошлого захода (чужие записи, чужие часы) хранить
        // нечего. Разделы — такие же transient: их создаёт страница из контейнера.
        builder.Services.AddTransient<BarberCabinetViewModel>();
        builder.Services.AddTransient<BarberBookingsViewModel>();
        builder.Services.AddTransient<BarberScheduleViewModel>();
        builder.Services.AddTransient<BarberSalonsViewModel>();
        builder.Services.AddTransient<BarberCabinetProfileViewModel>();
        builder.Services.AddTransient<BarberBookingsSectionView>();
        builder.Services.AddTransient<BarberScheduleView>();
        builder.Services.AddTransient<BarberSalonsView>();
        builder.Services.AddTransient<BarberProfileView>();

        // Дочерние экраны кабинета живут один визит и открываются поверх его стека:
        // форма записи клиента, перенос, заметка, онбординг, инбокс, правка карточки,
        // редактор услуги, создание и добавление салона и экран одного фото портфолио.
        // Свои чтения каждый из них повторяет сам (AutoRetryLoop регистрируется transient
        // выше), поэтому второго состояния между заходами нет.
        builder.Services.AddTransient<BarberBookingCreateViewModel>();
        builder.Services.AddTransient<BarberBookingRescheduleViewModel>();
        builder.Services.AddTransient<BarberBookingNoteViewModel>();
        builder.Services.AddTransient<BarberOnboardingViewModel>();
        builder.Services.AddTransient<BarberNotificationsViewModel>();
        builder.Services.AddTransient<BarberProfileEditViewModel>();
        builder.Services.AddTransient<BarberServiceEditorViewModel>();
        builder.Services.AddTransient<BarberSalonCreateViewModel>();
        builder.Services.AddTransient<BarberSalonAddViewModel>();
        builder.Services.AddTransient<BarberPhotoViewModel>();

        builder.Services.AddTransient<BarberBookingCreatePage>();
        builder.Services.AddTransient<BarberBookingReschedulePage>();
        builder.Services.AddTransient<BarberBookingNotePage>();
        builder.Services.AddTransient<BarberOnboardingPage>();
        builder.Services.AddTransient<BarberNotificationsPage>();
        builder.Services.AddTransient<BarberProfileEditPage>();
        builder.Services.AddTransient<BarberServiceEditorPage>();
        builder.Services.AddTransient<BarberSalonCreatePage>();
        builder.Services.AddTransient<BarberSalonAddPage>();
        builder.Services.AddTransient<BarberPhotoPage>();

        builder.Services.AddTransient<SearchPage>();
        builder.Services.AddTransient<BarberPage>();
        builder.Services.AddTransient<PortfolioPhotoPage>();
        builder.Services.AddTransient<RescheduleBookingPage>();
        builder.Services.AddTransient<ReviewBookingPage>();
        builder.Services.AddTransient<AccountProfilePage>();
        builder.Services.AddTransient<BarberRequestPage>();
        builder.Services.AddTransient<SecurityPage>();
        builder.Services.AddTransient<MyReviewsPage>();
        builder.Services.AddTransient<EditReviewPage>();
        builder.Services.AddTransient<RegisterPage>();
        builder.Services.AddTransient<ForgotPasswordPage>();
        builder.Services.AddTransient<AdminHubPage>();
        builder.Services.AddTransient<AdminReviewsPage>();
        builder.Services.AddTransient<AdminReviewPage>();
        builder.Services.AddTransient<AdminPhotosPage>();
        builder.Services.AddTransient<AdminMastersPage>();
        builder.Services.AddTransient<AdminMasterPage>();
        builder.Services.AddTransient<AdminRequestsPage>();
        builder.Services.AddTransient<AdminRequestPage>();

        // Кабинет мастера открывается строкой из «Профиля» и живёт поверх его стека:
        // возврат оставляет вкладку и загруженный профиль на месте. Его дочерние экраны
        // (запись, перенос, заметка, онбординг, уведомления, правка профиля, услуга,
        // создание салона) резолвятся из контейнера фабрикой маршрута и тоже живут один визит.
        builder.Services.AddTransient<BarberCabinetPage>();

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
