using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Domain.Push;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Push.RegisterPushDevice;

/// <summary>
/// Обработчик регистрации устройства: идемпотентный upsert по токену.
/// </summary>
/// <remarks>
/// <para>
/// Повторный <c>POST</c> с тем же токеном не создаёт вторую строку: хранилище обновляет
/// <c>last_seen_at</c>. Если тот же телефон вошёл в другой аккаунт, токен переходит
/// к новому владельцу — иначе push о чужом визите ушёл бы прежнему владельцу устройства.
/// </para>
/// <para>
/// Предел устройств (<c>Push:MaxTokensPerUser</c>) применяет хранилище в той же транзакции:
/// вставка и удаление самых старых токенов — одно действие, а не два запроса обработчика.
/// </para>
/// <para>
/// Токен в журнал не попадает: это идентификатор устройства, и его не должно быть ни в
/// Information, ни в ошибках (AGENT.md §13).
/// </para>
/// </remarks>
public sealed class RegisterPushDeviceHandler : ICommandHandler<RegisterPushDeviceCommand>
{
    private readonly IPushDeviceTokenRepository _tokens;
    private readonly PushOptions _options;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public RegisterPushDeviceHandler(
        IPushDeviceTokenRepository tokens,
        PushOptions options,
        ICurrentUser currentUser,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _tokens = tokens;
        _options = options;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Регистрирует токен текущего пользователя; повторный вызов идемпотентен.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        RegisterPushDeviceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);

        var userId = _currentUser.RequireUserId();
        if (userId.IsFailure)
        {
            return userId.Error;
        }

        var now = _timeProvider.GetUtcNow();

        var token = PushDeviceToken.Register(
            userId.Value,
            command.Request.Token,
            command.Request.Platform,
            now);
        if (token.IsFailure)
        {
            return token.Error;
        }

        var maxTokensPerUser = _options.MaxTokensPerUser > 0
            ? _options.MaxTokensPerUser
            : PushOptions.DEFAULT_MAX_TOKENS_PER_USER;

        return await _tokens.RegisterAsync(token.Value, maxTokensPerUser, cancellationToken);
    }
}
