using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.Admin.ApproveUserAvatar;

/// <summary>
/// Обработчик команды <see cref="ApproveUserAvatarCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>EnsureAdmin</c> вызывается до чтения агрегата: политика <c>AdminOnly</c> на эндпоинте —
/// не единственный вход, команду можно вызвать напрямую. Аноним получает
/// <c>401 auth.unauthorized</c>, пользователь без роли — <c>403 auth.forbidden</c>.
/// </para>
/// <para>
/// Одобрение и запись идут в одной транзакции <c>IUnitOfWork</c>: решение фиксирует
/// администратора и момент времени из <c>TimeProvider</c>, а при отказе домена контекст
/// остаётся чистым. Ошибки репозитория (<c>404 user.notFound</c>) и домена
/// (<c>409 user.avatarModeration.noAvatar</c> / <c>.invalidStatus</c>) возвращаются как есть.
/// </para>
/// </remarks>
public sealed class ApproveUserAvatarHandler
    : ICommandHandler<ApproveUserAvatarCommand>
{
    private readonly IUserRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ApproveUserAvatarHandler> _logger;

    /// <summary>
    /// Создаёт обработчик.
    /// </summary>
    /// <param name="repository">Пользователь: загрузка агрегата и сохранение.</param>
    /// <param name="unitOfWork">Транзакция решения и записи.</param>
    /// <param name="currentUser">Текущий пользователь: проверка роли администратора.</param>
    /// <param name="timeProvider">Часы: момент решения <c>ReviewedAt</c>.</param>
    /// <param name="logger">Журнал: только идентификаторы и решение.</param>
    public ApproveUserAvatarHandler(
        IUserRepository repository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<ApproveUserAvatarHandler> logger)
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
    /// Одобряет аватар и сохраняет агрегат вместе с решением администратора.
    /// </summary>
    /// <param name="command">Пользователь, чей аватар одобряется.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<UnitResult<Failure>> Handle(
        ApproveUserAvatarCommand command,
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

            var approved = user.ApproveAvatar(adminId, now);
            if (approved.IsFailure)
            {
                return approved.Error;
            }

            return await _repository.UpdateAsync(user, token);
        }, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        // URL, путь и причина в лог не попадают: только идентификаторы и решение.
        _logger.LogInformation(
            "Avatar of user {UserId} has been approved by administrator {AdminId}.",
            command.UserId,
            adminId);

        return UnitResult.Success<Failure>();
    }
}
