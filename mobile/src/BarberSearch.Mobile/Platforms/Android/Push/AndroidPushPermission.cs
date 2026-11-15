using BarberSearch.Mobile.Core.Push;
using Microsoft.Maui.ApplicationModel;

namespace BarberSearch.Mobile.Push;

/// <summary>
/// Разрешение на уведомления Android через MAUI Essentials.
/// </summary>
/// <remarks>
/// <para>
/// <c>Permissions.PostNotifications</c> есть в Essentials 10.0.110 и на Android 13+ показывает
/// системный диалог; на старых версиях разрешение считается выданным, потому что уведомления
/// там не требуют согласия. Плагин не нужен.
/// </para>
/// <para>
/// Оба метода переходят на главный поток: регистратор вызывает их с сетевого потока (вход,
/// обновление токена), а системный диалог можно показать только из UI-потока.
/// </para>
/// </remarks>
public sealed class AndroidPushPermission : IPushPermission
{
    /// <summary>
    /// Спрашивали ли разрешение в этом запуске.
    /// </summary>
    /// <remarks>
    /// Один системный диалог на запуск: Android после отказа второй раз его не показывает,
    /// а разные версии Essentials отвечают на первый <c>CheckStatusAsync</c> по-разному,
    /// поэтому решает не статус, а факт запроса. Включить уведомления после отказа можно
    /// только в настройках телефона.
    /// </remarks>
    private int _requested;

    /// <inheritdoc />
    public Task<bool> IsGrantedAsync(CancellationToken cancellationToken = default) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();

                return status == PermissionStatus.Granted;
            }
            catch (Exception)
            {
                return false;
            }
        });

    /// <inheritdoc />
    public Task<bool> RequestAsync(CancellationToken cancellationToken = default) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();

                if (status == PermissionStatus.Granted)
                {
                    return true;
                }

                if (Interlocked.Exchange(ref _requested, 1) == 1)
                {
                    return false;
                }

                status = await Permissions.RequestAsync<Permissions.PostNotifications>();

                return status == PermissionStatus.Granted;
            }
            catch (Exception)
            {
                return false;
            }
        });

    /// <inheritdoc />
    public void OpenSettings()
    {
        try
        {
            AppInfo.ShowSettingsUI();
        }
        catch (Exception)
        {
            // Экран настроек может отсутствовать на устройстве без соответствующего активити.
        }
    }
}
