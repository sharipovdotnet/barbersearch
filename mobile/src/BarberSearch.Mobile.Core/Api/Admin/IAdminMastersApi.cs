using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Mobile.Core.Api.Admin;

/// <summary>
/// Фильтр очереди мастеров: какие профили показать.
/// </summary>
/// <remarks>
/// Значения повторяют <c>AdminBarberStatusFilter</c> веба и строки, которые понимает
/// <c>GET api/admin/barbers?status=…</c>. Очередь проверки — значение по умолчанию:
/// именно её число показывает бейдж хаба.
/// </remarks>
public enum AdminMasterFilter
{
    /// <summary>
    /// Профили без подтверждения — очередь проверки.
    /// </summary>
    Pending,

    /// <summary>
    /// Все профили, включая подтверждённые и скрытые из поиска.
    /// </summary>
    All,

    /// <summary>
    /// Профили, скрытые из поиска.
    /// </summary>
    Hidden,
}

/// <summary>
/// Админское чтение мастеров и решение о подтверждении профиля.
/// </summary>
/// <remarks>
/// <para>
/// Список и карточка — отдельные контракты бэкенда: строка очереди не несёт контактов,
/// чек-листа и портфолио, поэтому карточка всегда читается запросом по идентификатору.
/// Список фильтруется на сервере (<c>status</c>), курсор непрозрачен и передаётся обратно
/// как есть.
/// </para>
/// <para>
/// Подтверждение и снятие подтверждения — записи: их не повторяет автоповтор, решение
/// принимает человек. Оба эндпоинта закрыты политикой <c>AdminOnly</c>, поэтому <c>403</c>
/// здесь означает «не администратор», а <c>401</c> — конец сессии.
/// </para>
/// </remarks>
public interface IAdminMastersApi
{
    /// <summary>
    /// Читает страницу мастеров выбранного фильтра.
    /// </summary>
    /// <param name="filter">Какие профили показать.</param>
    /// <param name="cursor">Курсор следующей страницы; <c>null</c> — первая страница.</param>
    /// <param name="pageSize">Размер страницы; сервер ограничивает его сверху.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница строк и курсор следующей.</returns>
    /// <exception cref="BarberSearchApiException">Сессия истекла, роль не администратор или API ответил ошибкой.</exception>
    Task<PagedResponse<AdminBarberResponse>> GetMastersAsync(
        AdminMasterFilter filter,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Читает карточку мастера любого статуса.
    /// </summary>
    /// <param name="masterId">Идентификатор профиля мастера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Контакты, готовность и портфолио профиля.</returns>
    /// <exception cref="BarberSearchApiException">Профиль не найден, сессия истекла или API ответил ошибкой.</exception>
    Task<AdminBarberDetailResponse> GetMasterAsync(
        Guid masterId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Подтверждает профиль мастера: он появляется в поиске и становится доступен для записи.
    /// </summary>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача записи; сервер отвечает <c>204</c>.</returns>
    /// <exception cref="BarberSearchApiException">Профиль скрыт, больше не существует, сессия истекла или роль не администратор.</exception>
    Task VerifyMasterAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Снимает подтверждение профиля: он исчезает из поиска и записи.
    /// </summary>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача записи; сервер отвечает <c>204</c>.</returns>
    /// <exception cref="BarberSearchApiException">Профиль больше не существует, сессия истекла или роль не администратор.</exception>
    Task UnverifyMasterAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default);
}
