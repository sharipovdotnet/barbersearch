using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Photos.Admin.GetAdminAvatarPhotos;
using BarberSearch.Application.Photos.Admin.GetAdminPortfolioPhotos;
using BarberSearch.Contracts.Admin;
using BarberSearch.Contracts.Common;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Очередь фотографий на проверку: аватары пользователей и работы портфолио.
/// </summary>
/// <remarks>
/// <para>
/// Оба чтения закрыты политикой <c>AdminOnly</c>; обработчики независимо от неё проверяют роль
/// через <c>EnsureAdmin</c>, потому что запрос отдаёт email владельцев. Ошибки параметров
/// приходят публичными кодами <c>admin.photos.*</c>, а не общим ответом биндера.
/// </para>
/// <para>
/// Очередь — только кадры <c>Pending</c>, в порядке поступления (FIFO): аватары по
/// <c>submittedAt, userId</c>, портфолио по <c>submittedAt, photoId</c>. Историю проверенных
/// кадров отдельным фильтром не отдаём: статус конкретного кадра виден в админских карточках
/// пользователя и барбера. Курсор непрозрачный и принадлежит своей вкладке.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/admin/photos")]
public class AdminPhotosController : ApiController
{
    /// <summary>
    /// Возвращает страницу аватаров, ждущих проверки.
    /// </summary>
    /// <remarks>
    /// Порядок — по времени поступления: <c>avatar_submitted_at ASC, id ASC</c> (FIFO).
    /// Поиск — подстрока по имени, фамилии или email владельца; пустая строка — без фильтра.
    /// </remarks>
    /// <param name="search">Поиск по имени или email владельца; пусто — без фильтра,
    /// длиннее 100 символов — <c>400 admin.photos.search</c>.</param>
    /// <param name="cursor">Непрозрачный курсор следующей страницы; битый —
    /// <c>400 admin.photos.cursor</c>.</param>
    /// <param name="pageSize">Размер страницы от 1 до 100; по умолчанию 20, вне диапазона —
    /// <c>400 admin.photos.pageSize</c>.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница <c>PagedResponse&lt;AdminAvatarPhotoResponse&gt;</c>; не администратор —
    /// <c>403 auth.forbidden</c>, аноним — <c>401</c>.</returns>
    [HttpGet("avatars")]
    public async Task<ActionResult<PagedResponse<AdminAvatarPhotoResponse>>> GetAvatars(
        [FromQuery] string? search,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromServices] IQueryHandler<
            PagedResponse<AdminAvatarPhotoResponse>,
            GetAdminAvatarPhotosQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetAdminAvatarPhotosQuery(search, cursor, pageSize),
            cancellationToken));

    /// <summary>
    /// Возвращает страницу работ портфолио, ждущих проверки.
    /// </summary>
    /// <remarks>
    /// Порядок — по времени создания: <c>created_at ASC, id ASC</c> (FIFO; отдельной метки
    /// поступления у портфолио нет). Поиск — подстрока по имени барбера или email владельца
    /// профиля; пустая строка — без фильтра.
    /// </remarks>
    /// <param name="search">Поиск по имени барбера или email владельца; пусто — без фильтра,
    /// длиннее 100 символов — <c>400 admin.photos.search</c>.</param>
    /// <param name="cursor">Непрозрачный курсор следующей страницы; битый —
    /// <c>400 admin.photos.cursor</c>.</param>
    /// <param name="pageSize">Размер страницы от 1 до 100; по умолчанию 20, вне диапазона —
    /// <c>400 admin.photos.pageSize</c>.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница <c>PagedResponse&lt;AdminPortfolioPhotoResponse&gt;</c>; не администратор —
    /// <c>403 auth.forbidden</c>, аноним — <c>401</c>.</returns>
    [HttpGet("portfolio")]
    public async Task<ActionResult<PagedResponse<AdminPortfolioPhotoResponse>>> GetPortfolio(
        [FromQuery] string? search,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromServices] IQueryHandler<
            PagedResponse<AdminPortfolioPhotoResponse>,
            GetAdminPortfolioPhotosQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetAdminPortfolioPhotosQuery(search, cursor, pageSize),
            cancellationToken));
}
