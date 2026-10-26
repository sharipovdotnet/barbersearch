using System.Security.Claims;
using BarberSearch.Application.Abstractions;
using BarberSearch.Application.RefreshTokens.IssueRefreshToken;
using BarberSearch.Application.RefreshTokens.RefreshSession;
using BarberSearch.Application.RefreshTokens.RevokeRefreshToken;
using BarberSearch.Application.Users.ChangePassword;
using BarberSearch.Application.Users.CreateUser;
using BarberSearch.Application.Users.LoginUser;
using BarberSearch.Application.Users.Passwords;
using BarberSearch.Contracts.Auth.ChangePassword;
using BarberSearch.Contracts.Auth.Csrf;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.Auth.Logout;
using BarberSearch.Contracts.Auth.Me;
using BarberSearch.Contracts.Auth.Refresh;
using BarberSearch.Contracts.Auth.Register;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Users;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
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
    private const string BearerPrefix = "Bearer ";

    /// <summary>
    /// Самостоятельная регистрация. Роль всегда <c>Customer</c>.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<ActionResult<CreatedResourceResponse>> Register(
        [FromBody] RegisterRequest request,
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

        // Роль берётся из кода, а не из тела: иначе первый же клиент зарегистрирует себе Admin.
        var command = new CreateUserCommand(
            request.Email,
            passwordHasher.HashPassword(null!, request.Password),
            request.FirstName,
            request.LastName,
            UserRole.Customer);

        return ToCreatedResult(await handler.Handle(command, cancellationToken));
    }

    /// <summary>
    /// Вход для мобильного или внешнего клиента: возвращает access-токен и refresh-токен в теле.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
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
    [EnableRateLimiting("auth")]
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
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
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
                "auth.refresh.missing",
                "A refresh token is required.")));
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

        var principal = ClaimsPrincipalFactory.CreatePrincipal(
            result.Value.User,
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = timeProvider.GetUtcNow().AddHours(authOptions.CookieHours)
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
    [EnableRateLimiting("auth")]
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
    /// Пользователь берётся из claims, поэтому чужой пароль сменить нельзя. Cookie-ветка
    /// требует antiforgery, Bearer-ветка — нет: у неё нет ambient-учётных данных.
    /// </remarks>
    [Authorize]
    [EnableRateLimiting("auth")]
    [HttpPost("change-password")]
    public async Task<ActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        [FromServices] ICommandHandler<ChangePasswordCommand> handler,
        [FromServices] AuthOptions authOptions,
        [FromServices] IWebHostEnvironment environment,
        [FromServices] IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        if (!IsBearerRequest())
        {
            var csrf = await ValidateAntiforgeryAsync(antiforgery);
            if (csrf is not null)
            {
                return csrf;
            }
        }

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
            .StartsWith(BearerPrefix, StringComparison.Ordinal);

    private async Task<ActionResult?> ValidateAntiforgeryAsync(IAntiforgery antiforgery)
    {
        var failure = await AntiforgeryValidation.ValidateAsync(HttpContext, antiforgery);

        return failure is null ? null : FailureProblemDetails.CreateResult(failure);
    }
}
