using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Users.Admin.BlockUserAvatar;
using BarberSearch.Application.Users.Admin.GetAdminUser;
using BarberSearch.Application.Users.Admin.GetAdminUsers;
using BarberSearch.Application.Users.Admin.UnblockUserAvatar;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Users.Admin;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Админские действия с пользователями: чтение списка и карточки, модерация аватара.
/// </summary>
/// <remarks>
/// Отдельный контроллер, потому что чтения пользователей не было вовсе: публичных
/// <c>GET api/users</c> нет и не будет — список содержит email, роль и статус доступа.
/// Все действия закрыты политикой <c>AdminOnly</c>. Остальные изменяющие операции
/// остаются в <c>UsersController</c> и работают без изменений.
/// <para>
/// Скрытие и возврат аватара живут здесь, рядом с чтениями: администратор видит флаг
/// в строке и карточке, а решение оставлять ли фото принимает на этой же поверхности.
/// </para>
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

    /// <summary>
    /// Скрывает аватар пользователя, не трогая доступ к аккаунту. Только администратор.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Скрытие — маска на чтении: URL и файл остаются, публичные чтения отдают
    /// <c>avatarUrl: null</c>, а раздача файла отвечает <c>404</c> всем, кроме администратора.
    /// Ни <c>IsActive</c>, ни роль, ни <c>SecurityStamp</c> не меняются: это модерация
    /// контента, а не блокировка пользователя. Строка <c>user.updated</c> с
    /// <c>changes.isAvatarBlocked</c> пишется тем же <c>SaveChanges</c>.
    /// </para>
    /// <para>
    /// Ответ <c>204</c>; повторное скрытие идемпотентно. Нет пользователя — <c>404
    /// user.notFound</c>, нет аватара — <c>409 user.blockAvatar.noAvatar</c>.
    /// </para>
    /// </remarks>
    /// <param name="userId">Пользователь, чей аватар скрывается.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpPost("{userId:guid}/avatar/block")]
    public async Task<ActionResult> BlockAvatar(
        Guid userId,
        [FromServices] ICommandHandler<BlockUserAvatarCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new BlockUserAvatarCommand(userId),
            cancellationToken));

    /// <summary>
    /// Возвращает скрытый аватар пользователя. Только администратор; повторный вызов
    /// идемпотентен.
    /// </summary>
    /// <remarks>
    /// Возврат показывает тот же кадр: URL и файл при скрытии не менялись. Ответ <c>204</c>;
    /// нет пользователя — <c>404 user.notFound</c>. Строка <c>user.updated</c> пишется только
    /// при реальном изменении флага.
    /// </remarks>
    /// <param name="userId">Пользователь, чей аватар возвращается.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpDelete("{userId:guid}/avatar/block")]
    public async Task<ActionResult> UnblockAvatar(
        Guid userId,
        [FromServices] ICommandHandler<UnblockUserAvatarCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UnblockUserAvatarCommand(userId),
            cancellationToken));
}
