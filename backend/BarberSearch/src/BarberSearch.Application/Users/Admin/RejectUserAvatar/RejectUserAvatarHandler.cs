using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.Admin.RejectUserAvatar;

/// <summary>
/// Обработчик команды <see cref="RejectUserAvatarCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>EnsureAdmin</c> вызывается до чтения агрегата: политика <c>AdminOnly</c> на эндпоинте —
/// не единственный вход. Аноним получает <c>401 auth.unauthorized</c>, пользователь без роли —
/// <c>403 auth.forbidden</c>.
/// </para>
/// <para>
/// Отклонение и запись идут в одной транзакции <c>IUnitOfWork</c>. Причина — текст
/// администратора: она сохраняется на пользователе, показывается владельцу, но в журнал
/// не пишется (там только идентификаторы и решение). Ошибки домена и репозитория
/// возвращаются как есть.
/// </para>
/// </remarks>
public sealed class RejectUserAvatarHandler
    : ICommandHandler<RejectUserAvatarCommand>
{
    private readonly IUserRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RejectUserAvatarHandler> _logger;

    /// <summary>
    /// Создаёт обработчик.
    /// </summary>
    /// <param name="repository">Пользователь: загрузка агрегата и сохранение.</param>
    /// <param name="unitOfWork">Транзакция решения и записи.</param>
    /// <param name="currentUser">Текущий пользователь: проверка роли администратора.</param>
    /// <param name="timeProvider">Часы: момент решения <c>ReviewedAt</c>.</param>
    /// <param name="logger">Журнал: только идентификаторы и решение.</param>
    public RejectUserAvatarHandler(
        IUserRepository repository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<RejectUserAvatarHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Отклоняет аватар с причиной и сохраняет агрегат вместе с решением администратора.
    /// </summary>
    /// <param name="command">Пользователь и причина отказа.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<UnitResult<Failure>> Handle(
        RejectUserAvatarCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var allowed = _currentUser.EnsureAdmin();
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var adminIdResult = _currentUser.RequireUserId();
        if (adminIdResult.IsFailure)
        {
            return adminIdResult.Error;
        }

        var adminId = adminIdResult.Value;
        var now = _timeProvider.GetUtcNow();

        var result = await _unitOfWork.ExecuteAsync(async token =>
        {
            var userResult = await _repository.GetByIdAsync(command.UserId, token);
            if (userResult.IsFailure)
            {
                return userResult.Error;
            }

            var user = userResult.Value;

            // Статус и причина проверяются доменом до записи; транзакция откатится,
            // и аватар останется в прежнем состоянии.
            var rejected = user.RejectAvatar(adminId, command.Reason, now);
            if (rejected.IsFailure)
            {
                return rejected.Error;
            }

            return await _repository.UpdateAsync(user, token);
        }, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        // Причина отказа — текст администратора, поэтому в журнал попадают только
        // идентификаторы и решение.
        _logger.LogInformation(
            "Avatar of user {UserId} has been rejected by administrator {AdminId}.",
            command.UserId,
            adminId);

        return UnitResult.Success<Failure>();
    }
}
