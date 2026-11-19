using BarberSearch.Contracts.BarberRequests.Admin;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Mobile.Core.Api.Admin;

/// <summary>
/// Фильтр списка заявок «Стать мастером»: очередь или все заявки.
/// </summary>
/// <remarks>
/// Значения — те же, что принимает <c>GET api/admin/barber-requests?status=</c>:
/// <c>pending</c> и <c>all</c>. Общего числа сервер не отдаёт, поэтому счётчиков у фильтра
/// нет: сузить выдачу можно только очередью или всеми заявками сразу.
/// </remarks>
public enum AdminRequestListFilter
{
    /// <summary>
    /// Неразобранные заявки, старые сверху: очередь, которую показывает бейдж хаба.
    /// </summary>
    Pending,

    /// <summary>
    /// Все заявки любого статуса, свежие сверху.
    /// </summary>
    All,
}

/// <summary>
/// Администраторские заявки на роль мастера: очередь, карточка и два решения.
/// </summary>
/// <remarks>
/// <para>
/// Ходит через клиент с <c>AuthenticatedHttpHandler</c>: роль администратора сервер берёт
/// из токена, и без неё ответ <c>403</c>. Это не «мои заявки» (<c>api/barber-requests/mine</c>):
/// здесь сервер добавляет заявителя (имя и email) и автора решения, поэтому контракт другой.
/// </para>
/// <para>
/// Чтения — курсорная страница и карточка по id; записи — одобрение и отказ. Причина отказа
/// обязательна (до 500 символов), её видит заявитель, поэтому пустое значение отклоняется
/// и на клиенте, и на сервере.
/// </para>
/// </remarks>
public interface IAdminRequestsApi
{
    /// <summary>
    /// Читает страницу заявок выбранного фильтра.
    /// </summary>
    /// <remarks>
    /// Курсор непрозрачен: он возвращается на сервер как есть и не разбирается. Пустой курсор
    /// в адрес не попадает — так запрашивается первая страница.
    /// </remarks>
    /// <param name="filter">Очередь <c>pending</c> или все заявки <c>all</c>.</param>
    /// <param name="cursor">Курсор следующей страницы либо <c>null</c> для первой.</param>
    /// <param name="pageSize">Размер страницы, 1..100.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница заявок и курсор следующей либо <c>null</c>.</returns>
    /// <exception cref="BarberSearchApiException">Сессия истекла, роль не администратор или API ответил ошибкой.</exception>
    Task<PagedResponse<AdminBarberRequestResponse>> GetRequestsAsync(
        AdminRequestListFilter filter,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Читает одну заявку любого статуса для карточки.
    /// </summary>
    /// <param name="requestId">Идентификатор заявки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Заявка с заявителем и решением администратора.</returns>
    /// <exception cref="BarberSearchApiException">Заявка не найдена, сессия истекла или API ответил ошибкой.</exception>
    Task<AdminBarberRequestResponse> GetRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Одобряет заявку: заявитель получает роль мастера, его сессии завершаются.
    /// </summary>
    /// <remarks>
    /// Ответ <c>200</c> несёт обновлённую заявку, но экран уходит назад и перечитывает очередь,
    /// поэтому тело не разбирается: успех — это статус ответа. Повторное решение сервер
    /// отвергает <c>409</c>.
    /// </remarks>
    /// <param name="requestId">Идентификатор заявки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача отправки решения.</returns>
    /// <exception cref="BarberSearchApiException">Заявку уже рассмотрели, сессия истекла или API ответил ошибкой.</exception>
    Task ApproveRequestAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Отклоняет заявку с обязательной причиной, не меняя роль заявителя.
    /// </summary>
    /// <remarks>
    /// Причина уходит на сервер без окружающих пробелов; пустая строка и причина длиннее
    /// 500 символов — <c>400</c>. Как и у одобрения, тело ответа не читается: успех — это
    /// статус.
    /// </remarks>
    /// <param name="requestId">Идентификатор заявки.</param>
    /// <param name="reason">Причина отказа, 1..500 символов.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача отправки решения.</returns>
    /// <exception cref="BarberSearchApiException">Причина пустая или слишком длинная, заявку уже рассмотрели, сессия истекла или API ответил ошибкой.</exception>
    Task RejectRequestAsync(
        Guid requestId,
        string reason,
        CancellationToken cancellationToken = default);
}
