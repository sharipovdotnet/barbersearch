using System.Net;
using System.Security.Claims;
using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Application.EmailVerifications.ConfirmEmailVerification;
using BarberSearch.Application.EmailVerifications.RequestEmailVerification;
using BarberSearch.Application.PasswordResets.ConfirmPasswordReset;
using BarberSearch.Application.PasswordResets.RequestPasswordReset;
using BarberSearch.Application.RefreshTokens;
using BarberSearch.Application.RefreshTokens.IssueRefreshToken;
using BarberSearch.Application.RefreshTokens.RefreshSession;
using BarberSearch.Application.RefreshTokens.RevokeRefreshToken;
using BarberSearch.Application.Users.ChangePassword;
using BarberSearch.Application.Users.CreateUser;
using BarberSearch.Application.Users.LoginUser;
using BarberSearch.Application.Users.Passwords;
using BarberSearch.Contracts.Auth.ChangePassword;
using BarberSearch.Contracts.Auth.Csrf;
using BarberSearch.Contracts.Auth.EmailVerification;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.Auth.Logout;
using BarberSearch.Contracts.Auth.Me;
using BarberSearch.Contracts.Auth.PasswordReset;
using BarberSearch.Contracts.Auth.Refresh;
using BarberSearch.Contracts.Auth.Register;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Users;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using BarberSearch.Web.Deployment;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Регистрация, вход, обновление пары и выход. Один и тот же пользователь получает либо
/// cookie (браузер), либо access-токен и refresh-токен в теле (мобильное приложение, внешний клиент).
/// </summary>
/// <remarks>
/// Обе ветки входа используют одну команду Application: пароль проверяется в одном месте,
/// а различается только способ выдачи учётных данных. Refresh-токен всегда хранится в базе
/// только как SHA-256; браузерная ветка кладёт открытое значение в HttpOnly-cookie,
/// внешний клиент получает его в теле ответа.
/// </remarks>
[ApiController]
[Route("api/auth")]
public class AuthController : ApiController
{
    /// <summary>
    /// Самостоятельная регистрация. Роль всегда <c>Customer</c>.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.AuthPolicyName)]
    // Регистрация анонимна и не несёт ambient-идентичности: привязывать токен не к чему.
    [SkipCookieAntiforgery]
    [HttpPost("register")]
    public async Task<ActionResult<CreatedResourceResponse>> Register(
        [FromBody] RegisterRequest request,
        [FromServices] ICommandHandler<Guid, CreateUserCommand> handler,
        [FromServices] ICommandHandler<RequestEmailVerificationCommand> requestEmailVerification,
        [FromServices] IPasswordHasher<User> passwordHasher,
        [FromServices] IPasswordPolicy passwordPolicy,
        [FromServices] ILogger<AuthController> logger,
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

        // Роль берётся из кода, а не из тела: иначе первый же клиент зарегистрирует себе Admin.
        // Согласие обязательно: без редакции Политики сервер не создаёт аккаунт. Время ставит
        // Application, а IP читается здесь — после UseForwardedHeaders это доверенный адрес.
        var command = new CreateUserCommand(
            request.Email,
            passwordHasher.HashPassword(null!, request.Password),
            request.FirstName,
            request.LastName,
            UserRole.Customer,
            request.ConsentVersion,
            GetClientIpAddress(),
            RequireConsent: true);

        var created = await handler.Handle(command, cancellationToken);

        if (created.IsFailure)
        {
            return ToCreatedResult(created);
        }

        // Письмо уходит сразу после создания пользователя, но сбой почты не валит
        // регистрацию: контракт ответа остаётся 201 { id }, ошибка — только в лог.
        // Отказ request-команды (почта не настроена, кулдаун, SMTP) здесь не возвращается.
        var verification = await requestEmailVerification.Handle(
            new RequestEmailVerificationCommand(created.Value),
            cancellationToken);

        if (verification.IsFailure)
        {
            logger.LogWarning(
                "The verification email for user {UserId} was not sent: {ErrorCodes}.",
                created.Value,
                string.Join(", ", verification.Error.Errors.Select(error => error.Code)));
        }

        return ToCreatedResult(created);
    }

    /// <summary>
    /// Отправляет письмо со ссылкой подтверждения email текущему пользователю.
    /// </summary>
    /// <remarks>
    /// Это cookie-мутация вошедшего пользователя, поэтому antiforgery-фильтр не отключается.
    /// Повторная отправка раньше 60 секунд — <c>409 auth.emailVerification.cooldown</c>,
    /// ненастроенная почта — <c>409 auth.emailVerification.unavailable</c>, сбой SMTP —
    /// <c>500 auth.emailVerification.sendFailed</c>.
    /// </remarks>
    [EnableRateLimiting(RateLimitSettings.AuthPolicyName)]
    [HttpPost("email-verification")]
    public async Task<ActionResult> RequestEmailVerification(
        [FromServices] ICurrentUser currentUser,
        [FromServices] ICommandHandler<RequestEmailVerificationCommand> handler,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        if (userId.IsFailure)
        {
            return FailureProblemDetails.CreateResult(userId.Error);
        }

        return ToActionResult(await handler.Handle(
            new RequestEmailVerificationCommand(userId.Value),
            cancellationToken));
    }

    /// <summary>
    /// Подтверждает адрес электронной почты по одноразовой ссылке из письма.
    /// </summary>
    /// <remarks>
    /// Действие анонимное: ссылку открывают в другом браузере, а секрет — сам токен.
    /// Antiforgery отключён осознанно: у запроса нет ambient-идентичности, и привязывать
    /// токен не к чему (тот же случай, что у регистрации). Повторный клик и уже
    /// подтверждённый адрес — <c>204</c>, битый или истёкший токен —
    /// <c>400 auth.emailVerification.invalid</c>.
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.AuthPolicyName)]
    [SkipCookieAntiforgery]
    [HttpPost("email-verification/confirm")]
    public async Task<ActionResult> ConfirmEmailVerification(
        [FromBody] ConfirmEmailVerificationRequest request,
        [FromServices] ICommandHandler<ConfirmEmailVerificationCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new ConfirmEmailVerificationCommand(request.Token ?? string.Empty),
            cancellationToken));

    /// <summary>
    /// Отправляет на указанный адрес ссылку для сброса пароля.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Действие анонимное и не опирается на текущую сессию: адрес берётся из тела, а не из
    /// <c>ICurrentUser</c>. Поэтому <c>[SkipCookieAntiforgery]</c> обязателен и безопасен —
    /// у запроса нет ambient-идентичности, к которой можно привязать antiforgery-пару, а
    /// секрет (одноразовый токен) живёт только в письме. Это тот же случай, что у регистрации
    /// и подтверждения email.
    /// </para>
    /// <para>
    /// Ответ <c>204</c> одинаков для существующего, несуществующего, неактивного, пустого и
    /// кривого адреса: по ответу нельзя выяснить, есть ли аккаунт. Тихий кулдаун 60 секунд
    /// и сбой SMTP тоже дают <c>204</c>. Единственный отказ — глобальный
    /// <c>409 auth.passwordReset.unavailable</c>, когда почта или <c>App:PublicUrl</c> не
    /// настроены; настоящий <c>429</c> приходит от rate limiter.
    /// </para>
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.AuthPolicyName)]
    // Запрос анонимный и не несёт ambient-идентичности: привязывать токен не к чему.
    [SkipCookieAntiforgery]
    [HttpPost("password-reset")]
    public async Task<ActionResult> RequestPasswordReset(
        [FromBody] RequestPasswordResetRequest request,
        [FromServices] ICommandHandler<RequestPasswordResetCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RequestPasswordResetCommand(request.Email ?? string.Empty),
            cancellationToken));

    /// <summary>
    /// Меняет пароль по одноразовой ссылке из письма и завершает все сессии пользователя.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Действие анонимное, как и подтверждение email: ссылку открывают в другом браузере или
    /// на другом устройстве, а секрет — сам одноразовый токен. Antiforgery отключён осознанно:
    /// у запроса нет cookie-идентичности, к которой его можно привязать.
    /// </para>
    /// <para>
    /// Использованный, отозванный, просроченный и неизвестный токен, отсутствующий или
    /// деактивированный пользователь дают один <c>400 auth.passwordReset.invalid</c>.
    /// Пароль, не прошедший политику, — <c>400 auth.password.*</c> с
    /// <c>invalidField = "newPassword"</c>; токен при этом не потребляется. Успех — <c>204</c>
    /// без тела: автоматического входа нет, клиент входит заново новым паролем.
    /// </para>
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.AuthPolicyName)]
    // Секрет — одноразовый токен из письма; ambient-идентичности у запроса нет.
    [SkipCookieAntiforgery]
    [HttpPost("password-reset/confirm")]
    public async Task<ActionResult> ConfirmPasswordReset(
        [FromBody] ConfirmPasswordResetRequest request,
        [FromServices] ICommandHandler<ConfirmPasswordResetCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new ConfirmPasswordResetCommand(
                request.Token ?? string.Empty,
                request.NewPassword ?? string.Empty),
            cancellationToken));

    /// <summary>
    /// Адрес клиента для записи согласия.
    /// </summary>
    /// <remarks>
    /// <c>RemoteIpAddress</c> уже учитывает <c>UseForwardedHeaders</c>: доверенный прокси
    /// подставляет настоящий адрес. Ручной разбор <c>X-Forwarded-For</c> не делается — иначе
    /// любой клиент подменил бы IP. IPv4-mapped форма (<c>::ffff:a.b.c.d</c>) приводится
    /// к IPv4, иначе один клиент дал бы два разных значения.
    /// </remarks>
    private IPAddress? GetClientIpAddress()
    {
        var address = HttpContext.Connection.RemoteIpAddress;

        return address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address;
    }

    /// <summary>
    /// Вход для мобильного или внешнего клиента: возвращает access-токен и refresh-токен в теле.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.AuthPolicyName)]
    // Body-ветка отдаёт токены в теле и не опирается на cookie; cookie-вход защищён атрибутом.
    [SkipCookieAntiforgery]
    [HttpPost("login")]
    public async Task<ActionResult<TokenResponse>> Login(
        [FromBody] LoginRequest request,
        [FromServices] ICommandHandler<AuthenticatedUser, LoginUserCommand> handler,
        [FromServices] ICommandHandler<IssuedRefreshToken, IssueRefreshTokenCommand> issueRefreshToken,
        [FromServices] JwtTokenIssuer issuer,
        [FromServices] AuthOptions authOptions,
        [FromServices] TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(
            new LoginUserCommand(request.Email, request.Password),
            cancellationToken);

        if (result.IsFailure)
        {
            return FailureProblemDetails.CreateResult(result.Error);
        }

        var refresh = await issueRefreshToken.Handle(
            new IssueRefreshTokenCommand(result.Value.Id, authOptions.RefreshTokenDays),
            cancellationToken);

        if (refresh.IsFailure)
        {
            return FailureProblemDetails.CreateResult(refresh.Error);
        }

        return Ok(CreateTokenResponse(result.Value, refresh.Value, issuer, timeProvider));
    }

    /// <summary>
    /// Вход для браузера: ставит HttpOnly-cookie сессии и HttpOnly-cookie refresh-токена,
    /// ничего не возвращает в теле.
    /// </summary>
    /// <remarks>
    /// Требует antiforgery-токен: без него возможен login CSRF, когда жертву молча
    /// залогинивают в аккаунт атакующего.
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.AuthPolicyName)]
    [ValidateAntiforgeryHeader]
    [HttpPost("login/cookie")]
    public async Task<ActionResult> LoginWithCookie(
        [FromBody] LoginRequest request,
        [FromServices] ICommandHandler<AuthenticatedUser, LoginUserCommand> handler,
        [FromServices] ICommandHandler<IssuedRefreshToken, IssueRefreshTokenCommand> issueRefreshToken,
        [FromServices] AuthOptions authOptions,
        [FromServices] IWebHostEnvironment environment,
        [FromServices] TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(
            new LoginUserCommand(request.Email, request.Password),
            cancellationToken);

        if (result.IsFailure)
        {
            return FailureProblemDetails.CreateResult(result.Error);
        }

        var refresh = await issueRefreshToken.Handle(
            new IssueRefreshTokenCommand(result.Value.Id, authOptions.RefreshTokenDays),
            cancellationToken);

        if (refresh.IsFailure)
        {
            return FailureProblemDetails.CreateResult(refresh.Error);
        }

        var principal = ClaimsPrincipalFactory.CreatePrincipal(
            result.Value,
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = request.RememberMe,
                ExpiresUtc = request.RememberMe
                    ? timeProvider.GetUtcNow().AddHours(authOptions.CookieHours)
                    : null
            });

        RefreshTokenCookie.Append(
            HttpContext,
            environment,
            authOptions,
            refresh.Value.Token,
            refresh.Value.ExpiresAt);

        return NoContent();
    }

    /// <summary>
    /// Меняет пару по refresh-токену. Браузер присылает cookie, внешний клиент — тело;
    /// в ответ браузер получает новые cookie, внешний клиент — новую пару в теле.
    /// </summary>
    /// <remarks>
    /// Ротация обязательна при каждом вызове. Повторно предъявленный заменённый токен —
    /// признак кражи: вся цепочка отзывается, клиент получает <c>401 auth.refresh.reused</c>.
    /// Cookie-ветка требует antiforgery: запрос опирается на cookie, а не на заголовок.
    /// <c>rememberMe</c> влияет только на cookie-ветку: <c>true</c> ставит постоянную
    /// access-cookie, <c>false</c> и отсутствие поля — сессионную. Refresh-токен в теле
    /// переключает действие на внешнюю ветку, и тогда cookie не ставятся вовсе.
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.AuthPolicyName)]
    // Cookie-ветка проверяет токен вручную, body-ветке CSRF не нужен: cookie не используются.
    [SkipCookieAntiforgery]
    [HttpPost("refresh")]
    public async Task<ActionResult> Refresh(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshTokenRequest? request,
        [FromServices] ICommandHandler<RefreshSessionResult, RefreshSessionCommand> handler,
        [FromServices] JwtTokenIssuer issuer,
        [FromServices] AuthOptions authOptions,
        [FromServices] IWebHostEnvironment environment,
        [FromServices] IAntiforgery antiforgery,
        [FromServices] TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var fromBody = !string.IsNullOrWhiteSpace(request?.RefreshToken);

        Request.Cookies.TryGetValue(RefreshTokenCookie.GetName(environment), out var cookieToken);

        var rawToken = fromBody ? request!.RefreshToken : cookieToken;

        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return FailureProblemDetails.CreateResult(new Failure(Error.Unauthorized(
                RefreshTokenErrorCodes.MISSING,
                Messages.REFRESH_TOKEN_REQUIRED)));
        }

        if (!fromBody)
        {
            var csrf = await ValidateAntiforgeryAsync(antiforgery);
            if (csrf is not null)
            {
                return csrf;
            }
        }

        var result = await handler.Handle(
            new RefreshSessionCommand(rawToken, authOptions.RefreshTokenDays),
            cancellationToken);

        if (result.IsFailure)
        {
            if (!fromBody)
            {
                // Старый cookie больше не действителен: убираем его, чтобы браузер не повторял
                // отозванный токен на каждом запросе.
                RefreshTokenCookie.Delete(HttpContext, environment, authOptions);
            }

            return FailureProblemDetails.CreateResult(result.Error);
        }

        var issued = new IssuedRefreshToken(
            result.Value.RefreshToken,
            result.Value.RefreshTokenExpiresAt);

        if (fromBody)
        {
            return Ok(CreateTokenResponse(result.Value.User, issued, issuer, timeProvider));
        }

        // Ветка не зависит от формы тела: наличие rememberMe не делает клиента внешним.
        var rememberMe = request?.RememberMe ?? false;

        var principal = ClaimsPrincipalFactory.CreatePrincipal(
            result.Value.User,
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                // «Запомнить меня» приходит через refresh и решает, переживёт ли access-cookie
                // перезапуск браузера. Без явного поля — сессионная cookie.
                IsPersistent = rememberMe,
                ExpiresUtc = rememberMe
                    ? timeProvider.GetUtcNow().AddHours(authOptions.CookieHours)
                    : null
            });

        RefreshTokenCookie.Append(
            HttpContext,
            environment,
            authOptions,
            issued.Token,
            issued.ExpiresAt);

        return NoContent();
    }

    /// <summary>
    /// Выход: гасит cookie сессии, отзывает предъявленный refresh-токен и удаляет refresh-cookie.
    /// </summary>
    /// <remarks>
    /// Logout остаётся анонимным намеренно: он должен работать с истёкшей сессией и с
    /// refresh-токеном в теле, когда cookie уже нет. Внешний клиент передаёт refresh-токен
    /// в теле или заголовке <c>Authorization: Bearer</c>. Cookie-ветка требует antiforgery;
    /// logout с телом или Bearer от неё не зависит. Как и остальные действия контроллера,
    /// logout ограничен политикой <c>auth</c>: 10 запросов в минуту на адрес клиента.
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.AuthPolicyName)]
    // Проверка зависит от ветки (cookie или тело) и уже выполнена внутри действия.
    [SkipCookieAntiforgery]
    [HttpPost("logout")]
    public async Task<ActionResult> Logout(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LogoutRequest? request,
        [FromServices] ICommandHandler<RevokeRefreshTokenCommand> handler,
        [FromServices] AuthOptions authOptions,
        [FromServices] IWebHostEnvironment environment,
        [FromServices] IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        var fromBody = !string.IsNullOrWhiteSpace(request?.RefreshToken);

        if (!fromBody && !IsBearerRequest())
        {
            var csrf = await ValidateAntiforgeryAsync(antiforgery);
            if (csrf is not null)
            {
                return csrf;
            }
        }

        Request.Cookies.TryGetValue(RefreshTokenCookie.GetName(environment), out var cookieToken);

        var rawToken = fromBody ? request!.RefreshToken : cookieToken;

        var revoked = await handler.Handle(
            new RevokeRefreshTokenCommand(rawToken),
            cancellationToken);

        if (revoked.IsFailure)
        {
            return FailureProblemDetails.CreateResult(revoked.Error);
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        RefreshTokenCookie.Delete(HttpContext, environment, authOptions);

        return NoContent();
    }

    /// <summary>
    /// Смена пароля вошедшим пользователем. Отзывает все refresh-токены пользователя.
    /// </summary>
    /// <remarks>
    /// Пользователь берётся из claims, поэтому чужой пароль сменить нельзя. Cookie-ветку
    /// закрывает глобальный antiforgery-фильтр; Bearer-ветка его не трогает — у неё нет
    /// ambient-учётных данных, а значит и CSRF.
    /// </remarks>
    [Authorize]
    [EnableRateLimiting(RateLimitSettings.AuthPolicyName)]
    [HttpPost("change-password")]
    public async Task<ActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        [FromServices] ICommandHandler<ChangePasswordCommand> handler,
        [FromServices] AuthOptions authOptions,
        [FromServices] IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimsPrincipalFactory.SUB_CLAIM), out var userId))
        {
            return Unauthorized();
        }

        var result = await handler.Handle(
            new ChangePasswordCommand(userId, request.CurrentPassword, request.NewPassword),
            cancellationToken);

        if (result.IsFailure)
        {
            return FailureProblemDetails.CreateResult(result.Error);
        }

        // Все refresh-токены отозваны обработчиком; cookie с мёртвым токеном больше не нужен.
        RefreshTokenCookie.Delete(HttpContext, environment, authOptions);

        return NoContent();
    }

    /// <summary>
    /// Текущий пользователь по данным аутентификации.
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public ActionResult<MeResponse> Me()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimsPrincipalFactory.SUB_CLAIM), out var id))
        {
            return Unauthorized();
        }

        var barberProfileId = Guid.TryParse(
            User.FindFirstValue(ClaimsPrincipalFactory.BARBER_PROFILE_ID_CLAIM),
            out var parsed)
            ? parsed
            : (Guid?)null;

        var isEmailVerified = bool.TryParse(
            User.FindFirstValue(ClaimsPrincipalFactory.EMAIL_VERIFIED_CLAIM),
            out var verified) && verified;

        return Ok(new MeResponse(
            id,
            User.FindFirstValue(ClaimsPrincipalFactory.EMAIL_CLAIM) ?? string.Empty,
            User.FindFirstValue(ClaimsPrincipalFactory.ROLE_CLAIM) ?? string.Empty,
            barberProfileId,
            isEmailVerified));
    }

    /// <summary>
    /// Выдаёт antiforgery-токен для cookie-входа, выхода, refresh и смены пароля.
    /// </summary>
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpGet("csrf")]
    public ActionResult<CsrfResponse> Csrf([FromServices] IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);

        return Ok(new CsrfResponse(tokens.RequestToken!));
    }

    private static TokenResponse CreateTokenResponse(
        AuthenticatedUser user,
        IssuedRefreshToken refresh,
        JwtTokenIssuer issuer,
        TimeProvider timeProvider)
    {
        var access = issuer.Issue(user);
        var refreshSeconds = (int)Math.Max(
            0,
            Math.Round((refresh.ExpiresAt - timeProvider.GetUtcNow()).TotalSeconds));

        return access with
        {
            RefreshToken = refresh.Token,
            RefreshTokenExpiresInSeconds = refreshSeconds
        };
    }

    private bool IsBearerRequest()
        => Request.Headers.Authorization
            .ToString()
            .StartsWith(AuthConstants.BEARER_PREFIX, StringComparison.Ordinal);

    private async Task<ActionResult?> ValidateAntiforgeryAsync(IAntiforgery antiforgery)
    {
        var failure = await AntiforgeryValidation.ValidateAsync(HttpContext, antiforgery);

        return failure is null ? null : FailureProblemDetails.CreateResult(failure);
    }
}
