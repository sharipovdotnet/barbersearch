namespace BarberSearch.Mobile.Core.Location;

/// <summary>
/// Название города по координатам устройства — для автоподстановки фильтра «Город».
/// </summary>
/// <remarks>
/// <para>
/// Интерфейс отдельный от <see cref="ILocationService"/>, потому что это два разных отказа:
/// координаты телефон может отдать, а обратное геокодирование — не суметь (нет сервиса
/// геокодера, таймаут сети на iOS). Экран подставляет город по умолчанию в обоих случаях
/// и не должен различать причины.
/// </para>
/// <para>
/// Реализация не бросает исключений на отказ платформы и возвращает <c>null</c>: город —
/// это подсказка поля, а не ответ API. Отмена вызывающего кода пробрасывается, чтобы
/// ушедший экран не подставил город в чужую форму.
/// </para>
/// </remarks>
public interface ICityLocator
{
    /// <summary>
    /// Определяет город по координатам.
    /// </summary>
    /// <param name="position">Точка устройства.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// Название города либо <c>null</c>, если геокодер его не знает.
    /// </returns>
    /// <exception cref="OperationCanceledException">Вызывающий код отменил чтение.</exception>
    Task<string?> GetCityAsync(GeoPosition position, CancellationToken cancellationToken = default);
}
