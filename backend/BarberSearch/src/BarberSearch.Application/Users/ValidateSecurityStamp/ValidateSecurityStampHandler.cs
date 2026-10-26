using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.ValidateSecurityStamp;

/// <summary>
/// Обработчик <see cref="ValidateSecurityStampQuery"/>: сверяет отпечаток и активность пользователя.
/// </summary>
/// <remarks>
/// <para>
/// Обращается к репозиторию ровно один раз — <see cref="IUserRepository.GetSecurityStampAsync"/>,
/// который читает узкий <c>SELECT is_active, security_stamp … WHERE id = …</c> без <c>Include</c>
/// и без отслеживания (R-11). Агрегат пользователя и коллекция избранного не загружаются.
/// </para>
/// <para>
/// «Пользователь не найден», «деактивирован» и «отпечаток устарел» — это не ошибки запроса,
/// а ожидаемый ответ <see langword="false"/>: вызывающий слой сам решает, как отозвать сессию
/// (JWT — <c>Fail</c>, cookie — <c>RejectPrincipal</c> + <c>SignOutAsync</c>).
/// Успешная проверка не логируется: она выполняется на каждом аутентифицированном запросе,
/// а предупреждения пишутся только при отказе — по образцу прежнего валидатора.
/// </para>
/// </remarks>
public class ValidateSecurityStampHandler : IQueryHandler<bool, ValidateSecurityStampQuery>
{
    private readonly IUserRepository _users;
    private readonly ILogger<ValidateSecurityStampHandler> _logger;

    public ValidateSecurityStampHandler(
        IUserRepository users,
        ILogger<ValidateSecurityStampHandler> logger)
    {
        _users = users;
        _logger = logger;
    }

    /// <summary>
    /// Возвращает <c>true</c>, только если пользователь существует, активен и его
    /// <c>SecurityStamp</c> совпадает с предъявленным.
    /// </summary>
    public async Task<Result<bool, Failure>> Handle(
        ValidateSecurityStampQuery query,
        CancellationToken cancellationToken)
    {
        var stampResult = await _users.GetSecurityStampAsync(query.UserId, cancellationToken);

        if (stampResult.IsFailure)
        {
            _logger.LogWarning(
                "Security stamp check failed: user {UserId} was not found.",
                query.UserId);

            return false;
        }

        var stamp = stampResult.Value;

        if (!stamp.IsActive)
        {
            _logger.LogWarning(
                "Security stamp check failed: user {UserId} is deactivated.",
                query.UserId);

            return false;
        }

        if (!string.Equals(stamp.SecurityStamp, query.SecurityStamp, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Security stamp check failed: the token of user {UserId} is stale.",
                query.UserId);

            return false;
        }

        return true;
    }
}
