using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Users.ActivateUser;
using BarberSearch.Application.Users.AddUserFavorite;
using BarberSearch.Application.Users.ChangeUserRole;
using BarberSearch.Application.Users.CreateUser;
using BarberSearch.Application.Users.DeactivateUser;
using BarberSearch.Application.Users.LinkUserBarberProfile;
using BarberSearch.Application.Users.MarkUserEmailAsVerified;
using BarberSearch.Application.Users.MarkUserPhoneAsVerified;
using BarberSearch.Application.Users.Passwords;
using BarberSearch.Application.Users.RecordUserLogin;
using BarberSearch.Application.Users.RemoveUserFavorite;
using BarberSearch.Application.Users.UpdateUserProfile;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Users.ChangeUserRole;
using BarberSearch.Contracts.Users.CreateUser;
using BarberSearch.Contracts.Users.UpdateUserProfile;
using BarberSearch.Domain.Users;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Пользователи: создание, профиль, роль, подтверждения, вход и избранное.
/// </summary>
/// <remarks>
/// Каждый action — ровно один сценарий Application. Хеширование пароля живёт здесь:
/// домен и Application получают готовый хеш (<c>CreateUserCommand.PasswordHash</c>),
/// поэтому открытый пароль дальше контроллера не уходит.
/// </remarks>
[ApiController]
[Route("api/users")]
public class UsersController : ApiController
{
    /// <summary>
    /// Создаёт пользователя. Доступно только администратору: самостоятельная регистрация
    /// живёт в <c>POST api/auth/register</c> и всегда создаёт <c>Customer</c>.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost]
    public async Task<ActionResult<CreatedResourceResponse>> Create(
        [FromBody] CreateUserRequest request,
        [FromServices] ICommandHandler<Guid, CreateUserCommand> handler,
        [FromServices] IPasswordHasher<User> passwordHasher,
        [FromServices] IPasswordPolicy passwordPolicy,
        CancellationToken cancellationToken)
    {
        // Политика проверяется до PBKDF2: 128 — потолок, чтобы большой пароль не жёг CPU.
        var policyFailure = passwordPolicy.Validate(
            request.Password,
            request.Email,
            request.FirstName,
            request.LastName,
            "password");
        if (policyFailure is not null)
        {
            return FailureProblemDetails.CreateResult(policyFailure);
        }

        // Хешеру нужен только пароль: в режиме IdentityV3 (по умолчанию) экземпляр
        // пользователя не участвует в вычислении хеша.
        var command = new CreateUserCommand(
            request.Email,
            passwordHasher.HashPassword(null!, request.Password),
            request.FirstName,
            request.LastName,
            request.Role);

        return ToCreatedResult(await handler.Handle(command, cancellationToken));
    }

    /// <summary>
    /// Обновляет профиль пользователя. Свой — сам пользователь, чужой — только администратор.
    /// </summary>
    [HttpPut("{userId:guid}/profile")]
    public async Task<ActionResult> UpdateProfile(
        Guid userId,
        [FromBody] UpdateUserProfileRequest request,
        [FromServices] ICommandHandler<UpdateUserProfileCommand> handler,
        CancellationToken cancellationToken)
    {
        return ToActionResult(await handler.Handle(
            new UpdateUserProfileCommand(
                userId,
                request.FirstName,
                request.LastName,
                request.PhoneNumber,
                request.AvatarUrl),
            cancellationToken));
    }

    /// <summary>
    /// Меняет роль пользователя. Только администратор.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPut("{userId:guid}/role")]
    public async Task<ActionResult> ChangeRole(
        Guid userId,
        [FromBody] ChangeUserRoleRequest request,
        [FromServices] ICommandHandler<ChangeUserRoleCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new ChangeUserRoleCommand(userId, request.Role),
            cancellationToken));

    /// <summary>
    /// Привязывает профиль барбера к пользователю. Только администратор.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPut("{userId:guid}/barber-profile/{barberProfileId:guid}")]
    public async Task<ActionResult> LinkBarberProfile(
        Guid userId,
        Guid barberProfileId,
        [FromServices] ICommandHandler<LinkUserBarberProfileCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new LinkUserBarberProfileCommand(userId, barberProfileId),
            cancellationToken));

    /// <summary>
    /// Активирует пользователя. Только администратор.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("{userId:guid}/activation")]
    public async Task<ActionResult> Activate(
        Guid userId,
        [FromServices] ICommandHandler<ActivateUserCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new ActivateUserCommand(userId), cancellationToken));

    /// <summary>
    /// Деактивирует пользователя. Только администратор.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpDelete("{userId:guid}/activation")]
    public async Task<ActionResult> Deactivate(
        Guid userId,
        [FromServices] ICommandHandler<DeactivateUserCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new DeactivateUserCommand(userId), cancellationToken));

    /// <summary>
    /// Помечает email подтверждённым. Пока только администратор: самостоятельное подтверждение
    /// появится вместе с письмом-ссылкой.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("{userId:guid}/email-verification")]
    public async Task<ActionResult> MarkEmailAsVerified(
        Guid userId,
        [FromServices] ICommandHandler<MarkUserEmailAsVerifiedCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new MarkUserEmailAsVerifiedCommand(userId),
            cancellationToken));

    /// <summary>
    /// Помечает телефон подтверждённым. Без сохранённого номера сценарий вернёт 400.
    /// Пока только администратор — как и подтверждение email.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("{userId:guid}/phone-verification")]
    public async Task<ActionResult> MarkPhoneAsVerified(
        Guid userId,
        [FromServices] ICommandHandler<MarkUserPhoneAsVerifiedCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new MarkUserPhoneAsVerifiedCommand(userId),
            cancellationToken));

    /// <summary>
    /// Отмечает вход пользователя: в профиле обновляется время последнего входа.
    /// </summary>
    /// <remarks>
    /// Оставлено только для администратора: обычный вход фиксируется самим логином,
    /// а анонимный вызов позволял бы подделать аудит входа.
    /// </remarks>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("{userId:guid}/logins")]
    public async Task<ActionResult> RecordLogin(
        Guid userId,
        [FromServices] ICommandHandler<RecordUserLoginCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new RecordUserLoginCommand(userId), cancellationToken));

    /// <summary>
    /// Добавляет барбера в избранное. Своё избранное — сам пользователь, чужое — только администратор. Повторное добавление — 409.
    /// </summary>
    [HttpPost("{userId:guid}/favorites/{barberProfileId:guid}")]
    public async Task<ActionResult> AddFavorite(
        Guid userId,
        Guid barberProfileId,
        [FromServices] ICommandHandler<AddUserFavoriteCommand> handler,
        CancellationToken cancellationToken)
    {
        return ToActionResult(await handler.Handle(
            new AddUserFavoriteCommand(userId, barberProfileId),
            cancellationToken));
    }

    /// <summary>
    /// Убирает барбера из избранного. Своё избранное — сам пользователь, чужое — только администратор. Если барбера там не было — 404.
    /// </summary>
    [HttpDelete("{userId:guid}/favorites/{barberProfileId:guid}")]
    public async Task<ActionResult> RemoveFavorite(
        Guid userId,
        Guid barberProfileId,
        [FromServices] ICommandHandler<RemoveUserFavoriteCommand> handler,
        CancellationToken cancellationToken)
    {
        return ToActionResult(await handler.Handle(
            new RemoveUserFavoriteCommand(userId, barberProfileId),
            cancellationToken));
    }
}
