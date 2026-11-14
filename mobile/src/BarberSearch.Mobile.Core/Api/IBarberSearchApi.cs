using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Mobile.Core.Api;

/// <summary>
/// Чтения API, которые нужны клиентскому приложению.
/// </summary>
/// <remarks>
/// Интерфейс существует ради тестов ViewModel: экраны зависят от него, а не от
/// <see cref="HttpClient"/>, поэтому проверяются без сети и без эмулятора. Набор методов растёт
/// вместе с экранами — «на всякий случай» сюда ничего не добавляется. Карточка и слоты —
/// публичные чтения, поэтому живут здесь и ходят на клиент без авторизационного обработчика.
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

    /// <summary>
    /// Публичная карточка барбера: шапка, услуги, салоны и специализации.
    /// </summary>
    /// <remarks>
    /// Выключенные услуги сервер отдаёт вместе с активными (<c>IsActive = false</c>), а скрытый
    /// или неподтверждённый профиль — <c>404</c>. Оба случая разбирает экран, а не клиент.
    /// </remarks>
    /// <param name="id">Идентификатор профиля барбера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Карточка профиля.</returns>
    /// <exception cref="BarberSearchApiException">Профиль не найден или API ответил ошибкой.</exception>
    Task<BarberProfileResponse> GetBarberProfileAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Свободные слоты барбера на локальную дату салона.
    /// </summary>
    /// <remarks>
    /// <paramref name="date"/> — календарная дата салона, а не инстант: без <paramref name="salonId"/>
    /// сервер берёт основной салон, без <paramref name="hairServiceId"/> шаг сетки — 30 минут.
    /// Пару <c>barberServiceId</c> + <c>durationMinutes</c> для брони отдаёт сам ответ.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля барбера.</param>
    /// <param name="slotDate">Локальная дата салона.</param>
    /// <param name="hairServiceId">Услуга справочника; <c>null</c> — «Любая услуга».</param>
    /// <param name="salonId">Салон; <c>null</c> — основной салон барбера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сетка слотов с поясом салона и длительностью шага.</returns>
    /// <exception cref="BarberSearchApiException">Услуга, салон или профиль недоступны.</exception>
    Task<BarberSlotsResponse> GetSlotsAsync(
        Guid barberProfileId,
        DateOnly slotDate,
        Guid? hairServiceId,
        Guid? salonId,
        CancellationToken cancellationToken = default);
}
