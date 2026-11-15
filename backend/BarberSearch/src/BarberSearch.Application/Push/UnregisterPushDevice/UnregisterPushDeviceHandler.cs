using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Push.UnregisterPushDevice;

/// <summary>
/// Обработчик снятия устройства: удаляет только собственный токен пользователя.
/// </summary>
/// <remarks>
/// <para>
/// Неизвестный или чужой токен — успех без эффекта. Это осознанно: ответ не должен
/// подсказывать, зарегистрирован ли токен у другого аккаунта, иначе перебором можно было бы
/// выяснить, какие устройства есть у чужого кабинета. Поэтому <c>404</c> здесь нет.
/// </para>
/// <para>
/// Пустой токен тоже успех: клиент мог прислать пустую строку из повреждённого хранилища,
/// и повторять ему нечего. Токен в журнал не пишется.
/// </para>
/// </remarks>
public sealed class UnregisterPushDeviceHandler : ICommandHandler<UnregisterPushDeviceCommand>
{
    private readonly IPushDeviceTokenRepository _tokens;
    private readonly ICurrentUser _currentUser;

    public UnregisterPushDeviceHandler(
        IPushDeviceTokenRepository tokens,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(currentUser);

        _tokens = tokens;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Снимает токен текущего пользователя; повторный вызов идемпотентен.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        UnregisterPushDeviceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);

        var userId = _currentUser.RequireUserId();
        if (userId.IsFailure)
        {
            return userId.Error;
        }

        var token = command.Request.Token?.Trim();

        if (string.IsNullOrEmpty(token))
        {
            return UnitResult.Success<Failure>();
        }

        var deleted = await _tokens.DeleteAsync(token, userId.Value, cancellationToken);
        if (deleted.IsFailure)
        {
            return deleted.Error;
        }

        return UnitResult.Success<Failure>();
    }
}
