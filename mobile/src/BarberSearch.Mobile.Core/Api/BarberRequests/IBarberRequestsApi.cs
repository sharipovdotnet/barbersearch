using BarberSearch.Contracts.BarberRequests;
using BarberSearch.Contracts.BarberRequests.CreateBarberRequest;
using BarberSearch.Contracts.BarberRequests.GetMyBarberRequest;

namespace BarberSearch.Mobile.Core.Api.BarberRequests;

/// <summary>
/// Заявка клиента на роль мастера: чтение своей заявки и её создание.
/// </summary>
/// <remarks>
/// <para>
/// Публичного списка заявок нет: клиент видит только свою последнюю заявку, админская очередь
/// живёт на вебе. Роль в тело <c>POST</c> не передаётся — <c>Barber</c> выдаёт только
/// администратор, а заявка ничего не разрешает.
/// </para>
/// <para>
/// Ходит через клиент с <c>AuthenticatedHttpHandler</c>: заголовок Bearer и продление пары —
/// его работа, поэтому <c>X-CSRF-TOKEN</c> здесь не нужен, antiforgery-фильтр пропускает
/// Bearer-запросы без него.
/// </para>
/// </remarks>
public interface IBarberRequestsApi
{
    /// <summary>
    /// Читает последнюю заявку текущего пользователя.
    /// </summary>
    /// <remarks>
    /// Отсутствие заявки — это <c>request: null</c>, а не <c>404</c>: форма «Стать мастером»
    /// открывается именно в этом состоянии, и ошибкой оно не является. Доступно любой роли,
    /// потому что заявка читается и тогда, когда роль уже сменилась.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Заявка либо <c>null</c> в <see cref="MyBarberRequestResponse.Request"/>.</returns>
    Task<MyBarberRequestResponse> GetMineAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Создаёт заявку клиента на роль мастера.
    /// </summary>
    /// <remarks>
    /// <c>201</c> возвращает саму заявку, поэтому экран показывает статус без второго чтения.
    /// Отказы разбирает вызывающий код по <c>StatusCode</c> и <c>ErrorCodes</c>
    /// <see cref="BarberSearchApiException"/>: <c>403</c> — не клиент, <c>409</c> — активная
    /// заявка уже есть, <c>400</c> — валидация полей, <c>429</c> — лимит.
    /// </remarks>
    /// <param name="request">Поля заявки; роль в них отсутствует.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Созданная заявка в статусе <c>Pending</c>.</returns>
    Task<BarberRequestResponse> CreateAsync(
        CreateBarberRequestRequest request,
        CancellationToken cancellationToken = default);
}
