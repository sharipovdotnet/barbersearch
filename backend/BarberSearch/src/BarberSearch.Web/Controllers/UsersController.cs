using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Users.ActivateUser;
using BarberSearch.Application.Users.AddUserFavorite;
using BarberSearch.Application.Users.ChangeUserRole;
using BarberSearch.Application.Users.CreateUser;
using BarberSearch.Application.Users.DeactivateUser;
using BarberSearch.Application.Users.DeleteUserAvatar;
using BarberSearch.Application.Users.GetMyProfile;
using BarberSearch.Application.Users.LinkUserBarberProfile;
using BarberSearch.Application.Users.MarkUserEmailAsVerified;
using BarberSearch.Application.Users.MarkUserPhoneAsVerified;
using BarberSearch.Application.Users.Passwords;
using BarberSearch.Application.Users.RecordUserLogin;
using BarberSearch.Application.Users.RemoveUserFavorite;
using BarberSearch.Application.Users.UpdateUserProfile;
using BarberSearch.Application.Users.UploadUserAvatar;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Users.ChangeUserRole;
using BarberSearch.Contracts.Users.CreateUser;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Contracts.Users.UpdateUserProfile;
using BarberSearch.Domain;
using BarberSearch.Domain.Users;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using BarberSearch.Web.Controllers.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Shared;

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
    /// Предел всего multipart-запроса: 6 МиБ, чтобы обвязка формы помещалась вокруг
    /// файла в 5 МиБ (<c>Constants.MAX_PHOTO_BYTES</c>). Kestrel отвечает <c>413</c> до
    /// действия; клиент обрабатывает и его, и код <c>user.uploadAvatar.tooLarge</c>.
    /// </summary>
    private const int MAX_AVATAR_REQUEST_BYTES = 6 * 1024 * 1024;

    /// <summary>
    /// Кусок чтения потока формы: файл читается порциями, а не целиком по <c>IFormFile.Length</c>.
    /// </summary>
    private const int AVATAR_READ_CHUNK_BYTES = 64 * 1024;

    private const string AVATAR_FILE_REQUIRED_CODE = "user.uploadAvatar.fileRequired";
    private const string AVATAR_TOO_LARGE_CODE = "user.uploadAvatar.tooLarge";

    private const string AVATAR_FILE_REQUIRED_MESSAGE = "An avatar file is required.";

    private static readonly string AvatarTooLargeMessage =
        $"The image file cannot be larger than {Constants.MAX_PHOTO_BYTES} bytes.";

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
    /// Профиль текущего пользователя для личного кабинета.
    /// </summary>
    /// <remarks>
    /// Идентификатор берётся из claims: чужой профиль этим адресом не прочитать. Ответ
    /// приходит из базы, поэтому после правки профиля кабинет видит актуальные имя,
    /// телефон и подтверждения, а не значения из выданной cookie.
    /// </remarks>
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<MyProfileResponse>> GetMe(
        [FromServices] IQueryHandler<MyProfileResponse, GetMyProfileQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new GetMyProfileQuery(), cancellationToken));

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
    /// Загружает аватар текущего пользователя (<c>multipart/form-data</c>, поле <c>file</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Аватар всегда свой: идентификатор берётся из claim'а <c>sub</c>, чужой загрузить нечем.
    /// Принимаются только JPEG, PNG и WebP; тип определяется по сигнатуре в
    /// <c>ImageFileInspector</c>, а не по имени файла или <c>Content-Type</c> клиента. Поток
    /// формы читается в память порциями с жёстким пределом <c>Constants.MAX_PHOTO_BYTES + 1</c>
    /// байт: <c>IFormFile.Length</c> — лишь подсказка от клиента, и доверять ей как пределу
    /// нельзя. Пустой файл — <c>400 user.uploadAvatar.fileRequired</c>, превышение —
    /// <c>400 user.uploadAvatar.tooLarge</c>; остальные проверки файла (сигнатура, размеры)
    /// приходят существующими кодами <c>barberProfile.uploadPhoto.*</c>.
    /// </para>
    /// <para>
    /// Успех — <c>201 { "avatarUrl": "&lt;относительный URL&gt;", "moderationStatus": "Pending" }</c>
    /// (<see cref="AvatarUploadResponse"/>) без <c>Location</c>: отдельного читаемого self-URI
    /// у аватара нет. Статус берётся из ответа обработчика, а не подставляется константой:
    /// премодерация — правило домена. Multipart-запрос — cookie-мутация, поэтому глобальный
    /// antiforgery-фильтр продолжает требовать <c>X-CSRF-TOKEN</c>; отдельной политики rate
    /// limiter нет — хватает глобальных 30 записей/мин.
    /// </para>
    /// </remarks>
    /// <param name="form">Форма с одним полем файла.</param>
    /// <param name="handler">Обработчик команды загрузки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [Authorize]
    [HttpPost("me/avatar")]
    [RequestSizeLimit(MAX_AVATAR_REQUEST_BYTES)]
    [RequestFormLimits(MultipartBodyLengthLimit = MAX_AVATAR_REQUEST_BYTES)]
    public async Task<ActionResult<AvatarUploadResponse>> UploadAvatar(
        [FromForm] UploadUserAvatarForm form,
        [FromServices] ICommandHandler<AvatarUploadResponse, UploadUserAvatarCommand> handler,
        CancellationToken cancellationToken)
    {
        // form?.File: при запросе без form-content биндер оставляет параметр пустым,
        // и это тот же «файла нет», а не 500.
        var file = form?.File;

        if (file is null || file.Length == 0)
        {
            return AvatarFileRequired();
        }

        await using var source = file.OpenReadStream();

        // Начальная ёмкость ограничена пределом: даже вредоносный Length не раздует буфер.
        using var buffer = new MemoryStream(
            capacity: (int)Math.Min(file.Length, Constants.MAX_PHOTO_BYTES));

        var chunk = new byte[AVATAR_READ_CHUNK_BYTES];
        var totalBytes = 0;

        while (true)
        {
            var read = await source.ReadAsync(chunk.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalBytes += read;

            // Останавливаемся на первом же байте сверх предела: целиком 6 МиБ не читаем.
            if (totalBytes > Constants.MAX_PHOTO_BYTES)
            {
                return AvatarTooLarge();
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        if (totalBytes == 0)
        {
            return AvatarFileRequired();
        }

        var uploaded = await handler.Handle(
            new UploadUserAvatarCommand(buffer.ToArray()),
            cancellationToken);

        return uploaded.IsSuccess
            ? StatusCode(
                StatusCodes.Status201Created,
                uploaded.Value)
            : FailureProblemDetails.CreateResult(uploaded.Error);
    }

    /// <summary>
    /// Удаляет аватар текущего пользователя. Идемпотентно: <c>204</c> приходит и тогда,
    /// когда аватара не было.
    /// </summary>
    /// <param name="handler">Обработчик команды удаления.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [Authorize]
    [HttpDelete("me/avatar")]
    public async Task<ActionResult> DeleteAvatar(
        [FromServices] ICommandHandler<DeleteUserAvatarCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new DeleteUserAvatarCommand(),
            cancellationToken));

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
    /// Помечает email подтверждённым вручную. Только администратор: это обход, когда письмо
    /// не дошло. Самостоятельное подтверждение — ссылкой из письма
    /// (<c>POST api/auth/email-verification/confirm</c>).
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

    /// <summary>
    /// <c>400 user.uploadAvatar.fileRequired</c>: файла нет или он пуст.
    /// </summary>
    private static ObjectResult AvatarFileRequired()
        => FailureProblemDetails.CreateResult(
            new Failure(Error.Validation(
                AVATAR_FILE_REQUIRED_CODE,
                AVATAR_FILE_REQUIRED_MESSAGE,
                "file")));

    /// <summary>
    /// <c>400 user.uploadAvatar.tooLarge</c>: файл больше 5 МиБ.
    /// </summary>
    private static ObjectResult AvatarTooLarge()
        => FailureProblemDetails.CreateResult(
            new Failure(Error.Validation(
                AVATAR_TOO_LARGE_CODE,
                AvatarTooLargeMessage,
                "file")));
}
