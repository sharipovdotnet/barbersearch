using BarberSearch.Mobile.Core.Location;
using Microsoft.Maui.Devices.Sensors;

namespace BarberSearch.Mobile.Devices;

/// <summary>
/// Город по координатам телефона — для автоподстановки фильтра «Город».
/// </summary>
/// <remarks>
/// <para>
/// Обратное геокодирование платформы (<c>Geocoding.Default</c>) переводит точку в адрес, из
/// которого берётся только <see cref="Placemark.Locality"/>: район и область городом не
/// считаются, а их подстановка дала бы фильтр, не совпадающий ни с одним салоном.
/// </para>
/// <para>
/// Чтение ограничено таймаутом: у <c>GetPlacemarksAsync</c> нет токена отмены, а сетевой
/// геокодер iOS может отвечать секундами. Таймаут, отказ платформы и пустой адрес — это
/// «города нет»: экран подставит город по умолчанию. Отмена вызывающего кода отличается от
/// таймаута и пробрасывается — экран уже ушёл.
/// </para>
/// </remarks>
public sealed class MauiCityLocator : ICityLocator
{
    /// <summary>
    /// Предел ожидания геокодера: поле города не должно пустовать дольше.
    /// </summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public async Task<string?> GetCityAsync(
        GeoPosition position,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var placemarks = await Geocoding.Default
                .GetPlacemarksAsync(position.Latitude, position.Longitude)
                .WaitAsync(ReadTimeout, cancellationToken);

            var placemark = placemarks?.FirstOrDefault();

            return CityName.Normalize(placemark?.Locality);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Экран закрыли или поиск отменили: это не «города нет».
            throw;
        }
        catch (Exception)
        {
            // Нет сервиса геокодера, нет сети, платформа ответила ошибкой или сработал
            // собственный таймаут. Для поля это один исход — города нет.
            return null;
        }
    }
}
