using System.Security.Claims;
using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Users.CreateUser;
using BarberSearch.Application.Users.LoginUser;
using BarberSearch.Contracts.Auth.Csrf;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.Auth.Me;
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
using Microsoft.AspNetCore.RateLimiting;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Регистрация, вход и выход. Один и тот же пользователь получает либо cookie (браузер),
/// либо access-токен (мобильное приложение, внешний клиент).
/// </summary>
/// <remarks>
/// Обе ветки входа используют одну команду Application: пароль проверяется в одном месте,
/// а различается только способ выдачи учётных данных. Открытый пароль дальше контроллера
/// не уходит — в команду он попадает только на входе, при регистрации хешируется здесь.
/// </remarks>
[ApiController]
[Route("api/auth")]
public class AuthController : ApiController
{
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
        CancellationToken cancellationToken)
    {
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
    /// Вход для мобильного или внешнего клиента: возвращает access-токен.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<ActionResult<TokenResponse>> Login(
        [FromBody] LoginRequest request,
        [FromServices] ICommandHandler<AuthenticatedUser, LoginUserCommand> handler,
        [FromServices] JwtTokenIssuer issuer,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(
            new LoginUserCommand(request.Email, request.Password),
            cancellationToken);

        if (result.IsFailure)
        {
            return FailureProblemDetails.CreateResult(result.Error);
        }

        return Ok(issuer.Issue(result.Value));
    }

    /// <summary>
    /// Вход для браузера: ставит HttpOnly-cookie и ничего не возвращает в теле.
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
        [FromServices] AuthOptions authOptions,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(
            new LoginUserCommand(request.Email, request.Password),
            cancellationToken);

        if (result.IsFailure)
        {
            return FailureProblemDetails.CreateResult(result.Error);
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
                    ? DateTimeOffset.UtcNow.AddHours(authOptions.CookieHours)
                    : null
            });

        return NoContent();
    }

    /// <summary>
    /// Выход из cookie-сессии. Bearer-клиенту выход не нужен: он просто забывает токен.
    /// </summary>
    [AllowAnonymous]
    [ValidateAntiforgeryHeader]
    [HttpPost("logout")]
    public async Task<ActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return NoContent();
    }

    /// <summary>
    /// Текущий пользователь по данным аутентификации.
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public ActionResult<MeResponse> Me()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimsPrincipalFactory.SubClaim), out var id))
        {
            return Unauthorized();
        }

        var barberProfileId = Guid.TryParse(
            User.FindFirstValue(ClaimsPrincipalFactory.BarberProfileIdClaim),
            out var parsed)
            ? parsed
            : (Guid?)null;

        var isEmailVerified = bool.TryParse(
            User.FindFirstValue(ClaimsPrincipalFactory.EmailVerifiedClaim),
            out var verified) && verified;

        return Ok(new MeResponse(
            id,
            User.FindFirstValue(ClaimsPrincipalFactory.EmailClaim) ?? string.Empty,
            User.FindFirstValue(ClaimsPrincipalFactory.RoleClaim) ?? string.Empty,
            barberProfileId,
            isEmailVerified));
    }

    /// <summary>
    /// Выдаёт antiforgery-токен для cookie-входа и выхода.
    /// </summary>
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpGet("csrf")]
    public ActionResult<CsrfResponse> Csrf([FromServices] IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);

        return Ok(new CsrfResponse(tokens.RequestToken!));
    }
}
