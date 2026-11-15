namespace BarberSearch.Mobile.Core.Location;

/// <summary>
/// Чтение геопозиции устройства для поиска «сначала ближайшие».
/// </summary>
/// <remarks>
/// <para>
/// Интерфейс существует ради тестов ViewModel: экран зависит от него, а не от
/// <c>Geolocation.Default</c>, поэтому проверяется без эмулятора и без разрешений ОС.
/// </para>
/// <para>
/// Реализация не бросает исключений на отказ в разрешении и ошибки платформы: она возвращает
/// <see cref="LocationStatus"/>. Отмена вызывающего кода (<see cref="OperationCanceledException"/>)
/// пробрасывается, потому что это не «нет геопозиции», а закрытый экран.
/// </para>
/// </remarks>
public interface ILocationService
{
    /// <summary>
    /// Запрашивает разрешение (если нужно) и читает последнюю известную или свежую точку.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Координаты либо причина, по которой их нет.</returns>
    /// <exception cref="OperationCanceledException">Вызывающий код отменил чтение.</exception>
    Task<LocationResult> GetPositionAsync(CancellationToken cancellationToken = default);
}
