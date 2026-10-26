using System.Security.Claims;
using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Users.ValidateSecurityStamp;

namespace BarberSearch.Web.Auth;

/// <summary>
/// Тонкий адаптер проверки учётных данных для cookie и JWT.
/// </summary>
/// <remarks>
/// Парсит claims <c>sub</c> и <c>security_stamp</c> и передаёт решение в Application —
/// <see cref="ValidateSecurityStampQuery"/>. Сама проверка (активность и совпадение отпечатка)
/// живёт в обработчике и покрыта юнит-тестами без тестового проекта Web.
/// <para>
/// Сервис scoped, потому что обработчик читает пользователя через scoped <c>DbContext</c>.
/// Смена пароля, роли и деактивация меняют <c>User.SecurityStamp</c>, поэтому уже выданный
/// access-токен (15 минут) и cookie (до 8 часов) перестают проходить проверку, не дожидаясь
/// истечения срока. Кэша нет намеренно: отзыв должен применяться на следующем же запросе,
/// а проверка — один узкий SELECT <c>is_active, security_stamp</c> по первичному ключу,
/// без <c>Include</c> и без отслеживания (R-11).
/// </para>
/// </remarks>
public sealed class SecurityStampValidator
{
    private readonly IQueryHandler<bool, ValidateSecurityStampQuery> _handler;
    private readonly ILogger<SecurityStampValidator> _logger;

    public SecurityStampValidator(
        IQueryHandler<bool, ValidateSecurityStampQuery> handler,
        ILogger<SecurityStampValidator> logger)
    {
        _handler = handler;
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
        var sub = principal?.FindFirstValue(ClaimsPrincipalFactory.SUB_CLAIM);

        if (!Guid.TryParse(sub, out var userId))
        {
            return false;
        }

        var claimedStamp = principal!.FindFirstValue(ClaimsPrincipalFactory.SECURITY_STAMP_CLAIM);

        if (string.IsNullOrWhiteSpace(claimedStamp))
        {
            _logger.LogWarning(
                "Security stamp check failed: the token of user {UserId} carries no stamp.",
                userId);

            return false;
        }

        var result = await _handler.Handle(
            new ValidateSecurityStampQuery(userId, claimedStamp),
            cancellationToken);

        return result.IsSuccess && result.Value;
    }
}
