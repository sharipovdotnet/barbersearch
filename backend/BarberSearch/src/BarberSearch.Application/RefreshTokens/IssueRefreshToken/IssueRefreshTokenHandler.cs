using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.RefreshTokens;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.RefreshTokens.IssueRefreshToken;

/// <summary>
/// Создаёт refresh-токен и сохраняет только его SHA-256.
/// </summary>
/// <remarks>
/// Используется обеими ветками входа: cookie-браузер получает токен в HttpOnly-cookie,
/// внешний клиент — в теле ответа. Срок жизни приходит из <c>Auth:RefreshTokenDays</c>
/// и проверяется по границам домена (7–30 дней).
/// </remarks>
public class IssueRefreshTokenHandler : ICommandHandler<IssuedRefreshToken, IssueRefreshTokenCommand>
{
    private readonly IRefreshTokenRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IssueRefreshTokenHandler> _logger;

    public IssueRefreshTokenHandler(
        IRefreshTokenRepository repository,
        TimeProvider timeProvider,
        ILogger<IssueRefreshTokenHandler> logger)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Генерирует открытый токен, сохраняет его хеш и возвращает открытое значение вызывающему.
    /// </summary>
    public async Task<Result<IssuedRefreshToken, Failure>> Handle(
        IssueRefreshTokenCommand command,
        CancellationToken cancellationToken)
    {
        if (RefreshTokenLifetime.Validate(command.LifetimeDays) is { } lifetimeError)
        {
            return lifetimeError;
        }

        var now = _timeProvider.GetUtcNow();
        var rawToken = RefreshTokenGenerator.CreateToken();

        var tokenResult = RefreshToken.Create(
            command.UserId,
            RefreshTokenGenerator.ComputeHash(rawToken),
            now.AddDays(command.LifetimeDays),
            now);

        if (tokenResult.IsFailure)
        {
            return tokenResult.Error;
        }

        var token = tokenResult.Value;

        var saved = await _repository.AddAsync(token, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Refresh token issued for user {UserId}, expires at {ExpiresAt}.",
            token.UserId,
            token.ExpiresAt);

        return new IssuedRefreshToken(rawToken, token.ExpiresAt);
    }
}
