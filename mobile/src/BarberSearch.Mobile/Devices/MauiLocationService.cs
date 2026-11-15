using BarberSearch.Mobile.Core.Location;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;

namespace BarberSearch.Mobile.Devices;

/// <summary>
/// Геопозиция телефона для поиска «сначала ближайшие».
/// </summary>
/// <remarks>
/// <para>
/// Точность <see cref="GeolocationAccuracy.Medium"/>: для порядка «кто ближе» хватает сотен
/// метров, а высокая точность на Android заметно расходует батарею и дольше ищет спутники.
/// Свежий последний известный фикс (до пяти минут) берётся без запуска GPS: человек не мог
/// уехать далеко за это время, а экран поиска не должен ждать спутники.
/// </para>
/// <para>
/// Каждое чтение ограничено таймаутом: <c>GetLocationAsync</c> без отмены может висеть вечно,
/// если GPS выключен или телефон в помещении. Отмена вызывающего кода пробрасывается —
/// это закрытый экран, а не «геопозиции нет».
/// </para>
/// <para>
/// iOS может дать «примерную» точность (километры): для сортировки по расстоянию это
/// приемлемо, поэтому ограничение не проверяется. Android <c>IsFromMockProvider</c> тоже
/// не проверяется: подмена координат не даёт доступа к данным, а только меняет порядок.
/// </para>
/// </remarks>
public sealed class MauiLocationService : ILocationService
{
    private static readonly TimeSpan FreshFixLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public async Task<LocationResult> GetPositionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Разрешение запрашивается здесь, а не на старте приложения: диалог ОС появляется
            // в момент, когда человек сам попросил найти мастера рядом.
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }

            if (status != PermissionStatus.Granted)
            {
                return LocationResult.Denied();
            }

            var cached = await Geolocation.Default.GetLastKnownLocationAsync();

            if (cached is not null && DateTimeOffset.UtcNow - cached.Timestamp < FreshFixLifetime)
            {
                return LocationResult.Ok(new GeoPosition(cached.Latitude, cached.Longitude));
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ReadTimeout);

            var request = new GeolocationRequest(GeolocationAccuracy.Medium, ReadTimeout);
            var location = await Geolocation.Default.GetLocationAsync(request, timeout.Token);

            return location is null
                ? LocationResult.Unavailable()
                : LocationResult.Ok(new GeoPosition(location.Latitude, location.Longitude));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Экран закрыли или поиск отменили: это не отказ в геопозиции.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Сработал собственный таймаут чтения — координат просто нет.
            return LocationResult.Unavailable();
        }
        catch (PermissionException)
        {
            return LocationResult.Denied();
        }
        catch (Exception)
        {
            // Нет GPS, выключены службы геолокации или платформа ответила ошибкой. Отмена
            // вызывающего кода сюда не попадает — она перехвачена выше и пробрасывается.
            return LocationResult.Unavailable();
        }
    }
}
