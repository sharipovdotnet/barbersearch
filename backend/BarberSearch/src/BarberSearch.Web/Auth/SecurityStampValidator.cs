using System.Security.Claims;
using BarberSearch.Application.Users;

namespace BarberSearch.Web.Auth;

/// <summary>
/// Проверяет, что выданные учётные данные ещё действительны: claim <c>security_stamp</c>
/// в токене совпадает с сохранённым у пользователя, а сам пользователь активен.
/// </summary>
/// <remarks>
/// Сервис scoped, потому что читает пользователя через <see cref="IUserRepository"/> и scoped
/// <c>DbContext</c>. Смена пароля, роли и деактивация меняют <c>User.SecurityStamp</c>, поэтому
/// уже выданный access-токен (15 минут) и cookie (до 8 часов) перестают проходить проверку
/// не дожидаясь истечения срока. Кэша нет намеренно: отзыв должен применяться на следующем же
/// запросе, а проверка — это один SELECT по первичному ключу.
/// </remarks>
public sealed class SecurityStampValidator
{
    private readonly IUserRepository _users;
    private readonly ILogger<SecurityStampValidator> _logger;

    public SecurityStampValidator(
        IUserRepository users,
        ILogger<SecurityStampValidator> logger)
    {
        _users = users;
        _logger = logger;
    }

    /// <summary>
    /// Возвращает <c>true</c>, только если пользователь существует, активен и его
    /// <c>SecurityStamp</c> совпадает с claim'ом токена.
    /// </summary>
    /// <param name="principal">Уже проверенная подпись токена или cookie.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    public async Task<bool> ValidateAsync(
        ClaimsPrincipal? principal,
        CancellationToken cancellationToken = default)
    {
        var sub = principal?.FindFirstValue(ClaimsPrincipalFactory.SubClaim);

        if (!Guid.TryParse(sub, out var userId))
        {
            return false;
        }

        var claimedStamp = principal!.FindFirstValue(ClaimsPrincipalFactory.SecurityStampClaim);

        if (string.IsNullOrWhiteSpace(claimedStamp))
        {
            _logger.LogWarning(
                "Security stamp check failed: the token of user {UserId} carries no stamp.",
                userId);

            return false;
        }

        var userResult = await _users.GetByIdAsync(userId, cancellationToken);

        if (userResult.IsFailure)
        {
            _logger.LogWarning(
                "Security stamp check failed: user {UserId} was not found.",
                userId);

            return false;
        }

        var user = userResult.Value;

        if (!user.IsActive)
        {
            _logger.LogWarning(
                "Security stamp check failed: user {UserId} is deactivated.",
                userId);

            return false;
        }

        if (!string.Equals(user.SecurityStamp, claimedStamp, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Security stamp check failed: the token of user {UserId} is stale.",
                userId);

            return false;
        }

        return true;
    }
}
