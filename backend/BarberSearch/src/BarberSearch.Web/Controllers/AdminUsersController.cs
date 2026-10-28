using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Users.Admin.GetAdminUser;
using BarberSearch.Application.Users.Admin.GetAdminUsers;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Users.Admin;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Админское чтение пользователей: список с фильтрами и карточка с профилем барбера.
/// </summary>
/// <remarks>
/// Отдельный контроллер, потому что чтения пользователей не было вовсе: публичных
/// <c>GET api/users</c> нет и не будет — список содержит email, роль и статус доступа.
/// Все действия закрыты политикой <c>AdminOnly</c>. Изменяющие операции остаются
/// в <c>UsersController</c> и работают без изменений.
/// </remarks>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/admin/users")]
public class AdminUsersController : ApiController
{
    /// <summary>
    /// Возвращает страницу пользователей: фильтры статуса и роли, поиск по имени и email.
    /// </summary>
    /// <param name="status">Фильтр <c>all</c> (по умолчанию), <c>active</c>, <c>blocked</c>
    /// или <c>unverified</c>.</param>
    /// <param name="role">Фильтр <c>all</c> (по умолчанию) либо <c>Customer</c>, <c>Barber</c>, <c>Admin</c>.</param>
    /// <param name="search">Поиск по имени, фамилии или email.</param>
    /// <param name="cursor">Непрозрачный курсор следующей страницы.</param>
    /// <param name="pageSize">Размер страницы; по умолчанию 20, максимум 100.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<AdminUserResponse>>> Get(
        [FromQuery] string? status,
        [FromQuery] string? role,
        [FromQuery] string? search,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromServices] IQueryHandler<PagedResponse<AdminUserResponse>, GetAdminUsersQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetAdminUsersQuery(status, role, search, cursor, pageSize),
            cancellationToken));

    /// <summary>
    /// Возвращает одного пользователя любого статуса для админской карточки.
    /// </summary>
    /// <param name="userId">Идентификатор пользователя.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet("{userId:guid}")]
    public async Task<ActionResult<AdminUserDetailResponse>> GetById(
        Guid userId,
        [FromServices] IQueryHandler<AdminUserDetailResponse, GetAdminUserQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetAdminUserQuery(userId),
            cancellationToken));
}
