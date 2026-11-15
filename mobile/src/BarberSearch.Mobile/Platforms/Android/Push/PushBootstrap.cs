using Android.Content;
using BarberSearch.Mobile.Core.Push;
using Firebase;

namespace BarberSearch.Mobile.Push;

/// <summary>
/// Инициализация FCM из <c>push.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Конфигурация читается синхронно из asset'ов приложения на старте: Firebase нужен до
/// первого обращения к токену, а асинхронный запуск оставил бы окно, в котором регистрация
/// молча не работает. Файла нет, он битый, <c>enabled</c> выключен или пусто поле — канал
/// считается ненастроенным, <see cref="IsReady"/> остаётся <c>false</c>, и ни одно
/// разрешение не запрашивается.
/// </para>
/// <para>
/// <c>FirebaseOptions</c> собираются программно, а не через <c>google-services.json</c>:
/// файл конфигурации Firebase в репозиторий не попадает, у каждого стенда он свой, а
/// приложение без него обязано собираться и работать как раньше. <c>FirebaseInitProvider</c>
/// из пакета остаётся в манифесте: без ресурсов Google Services он лишь пишет строку в
/// журнал и ничего не ломает.
/// </para>
/// </remarks>
public static class PushBootstrap
{
    /// <summary>
    /// Имя ресурса конфигурации; сам файл в git не хранится, рядом лежит шаблон.
    /// </summary>
    public const string ConfigAssetName = "push.json";

    /// <summary>
    /// Инициализирован ли Firebase и можно ли получать токен устройства.
    /// </summary>
    public static bool IsReady { get; private set; }

    /// <summary>
    /// Действующие настройки канала; <see cref="PushOptions.Disabled"/> без конфигурации.
    /// </summary>
    public static PushOptions Options { get; private set; } = PushOptions.Disabled;

    /// <summary>
    /// Читает <c>push.json</c> и инициализирует Firebase.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Вызывается один раз из композиционного корня. Любой сбой — отсутствие ресурса,
    /// несовместимый проект Firebase, отсутствие сервисов Google Play — превращается
    /// в <see cref="PushOptions.Disabled"/>, а не в исключение: старт приложения не должен
    /// зависеть от конфигурации push.
    /// </para>
    /// <para>
    /// Канал уведомлений здесь не создаётся: его имя и описание видны в настройках телефона
    /// и должны следовать языку интерфейса, а язык инициализируется позже, в конструкторе
    /// <c>App</c>. Канал создаёт <see cref="AndroidNotifications"/> уже с готовыми строками.
    /// </para>
    /// </remarks>
    /// <returns>Настройки, которые дальше живут в DI как singleton.</returns>
    public static PushOptions Initialize()
    {
        try
        {
            var context = Android.App.Application.Context;
            var options = PushOptions.Parse(ReadConfig(context));

            if (!options.Enabled)
            {
                return Disable();
            }

            var firebaseOptions = new FirebaseOptions.Builder()
                .SetProjectId(options.ProjectId)
                .SetApplicationId(options.ApplicationId)
                .SetApiKey(options.ApiKey)
                .SetGcmSenderId(options.SenderId)
                .Build();

            // InitializeApp возвращает null, если приложение Firebase уже поднято
            // (например, провайдером из пакета): тогда берём существующий экземпляр.
            var app = FirebaseApp.InitializeApp(context, firebaseOptions) ?? FirebaseApp.Instance;

            if (app is null)
            {
                return Disable();
            }

            Options = options;
            IsReady = true;

            return Options;
        }
        catch (Exception)
        {
            return Disable();
        }
    }

    /// <summary>
    /// Читает ресурс конфигурации; отсутствие файла — не ошибка, а «push не настроен».
    /// </summary>
    private static string? ReadConfig(Context context)
    {
        var assets = context.Assets;

        if (assets is null)
        {
            return null;
        }

        using var stream = assets.Open(ConfigAssetName);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private static PushOptions Disable()
    {
        Options = PushOptions.Disabled;
        IsReady = false;

        return Options;
    }
}
