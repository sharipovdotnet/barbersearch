using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Mobile.Core.Api;

/// <summary>
/// Чтения API, которые нужны клиентскому приложению.
/// </summary>
/// <remarks>
/// Интерфейс существует ради тестов ViewModel: экраны зависят от него, а не от
/// <see cref="HttpClient"/>, поэтому проверяются без сети и без эмулятора. Набор методов растёт
/// вместе с экранами — «на всякий случай» сюда ничего не добавляется.
/// </remarks>
public interface IBarberSearchApi
{
    /// <summary>
    /// Ищет барберов с фильтром по городу и keyset-пагинацией.
    /// </summary>
    /// <param name="city">Город салона; пусто — без фильтра.</param>
    /// <param name="cursor">Непрозрачный курсор следующей страницы из предыдущего ответа.</param>
    /// <param name="pageSize">Размер страницы; сервер по умолчанию отдаёт 20.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница выдачи; <c>NextCursor = null</c> означает последнюю.</returns>
    /// <exception cref="BarberSearchApiException">API ответил ошибкой.</exception>
    Task<PagedResponse<BarberSearchItemResponse>> SearchBarbersAsync(
        string? city,
        string? cursor = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default);
}
