using BarberSearch.Mobile.Core.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace BarberSearch.Mobile.Localization;

/// <summary>
/// Пересобирает Shell после смены языка.
/// </summary>
/// <remarks>
/// <para>
/// Экран и ViewModel собираются один раз при открытии, а подписи в них — обычные строки.
/// Чтобы английский появился везде сразу, приложение создаёт Shell и содержимое вкладок
/// заново из контейнера: это аналог перезагрузки страницы в вебе, только с сохранением
/// выбранной вкладки.
/// </para>
/// <para>
/// Новый Shell берётся из DI (<c>transient</c>), поэтому страницы и их ViewModel получают
/// новый язык. Маршруты Shell регистрируются один раз в <see cref="AppShell"/>; здесь они
/// не трогаются.
/// </para>
/// </remarks>
public static class ShellReloader
{
    private static bool _isReloading;

    /// <summary>
    /// Создаёт Shell заново и возвращает на вкладку «Профиль».
    /// </summary>
    /// <returns>Задача перестройки интерфейса.</returns>
    public static async Task RebuildAsync()
    {
        if (_isReloading)
        {
            return;
        }

        var application = Application.Current;
        var window = application is { Windows.Count: > 0 } ? application.Windows[0] : null;
        var services = application?.Handler?.MauiContext?.Services
            ?? IPlatformApplication.Current?.Services;

        if (application is null || window is null || services is null)
        {
            return;
        }

        _isReloading = true;

        try
        {
            var shell = services.GetRequiredService<AppShell>();

            window.Page = shell;

            // Переключатель живёт на вкладке «Профиль»: после пересборки возвращаемся туда,
            // а не на «Поиск» по умолчанию.
            await shell.GoToAsync("//profile");
        }
        finally
        {
            _isReloading = false;
        }
    }
}
