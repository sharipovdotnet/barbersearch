using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.Admin.BlockUserAvatar;

/// <summary>
/// Обработчик команды <see cref="BlockUserAvatarCommand"/>.
/// </summary>
/// <remarks>
/// Требует администратора до чтения: политика <c>AdminOnly</c> на эндпоинте — не единственный
/// вход, команду можно вызвать напрямую. Ошибки репозитория возвращаются как есть
/// (<c>404 user.notFound</c>), ошибка домена (<c>409 user.blockAvatar.noAvatar</c>) — тоже.
/// Строка аудита <c>user.updated</c> появляется в том же <c>SaveChanges</c>, что и изменение
/// флага, поэтому отдельного вызова аудита здесь нет.
/// </remarks>
public sealed class BlockUserAvatarHandler
    : ICommandHandler<BlockUserAvatarCommand>
{
    private readonly IUserRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<BlockUserAvatarHandler> _logger;

    /// <summary>
    /// Создаёт обработчик.
    /// </summary>
    /// <param name="repository">Пользователь: загрузка агрегата и сохранение.</param>
    /// <param name="currentUser">Текущий пользователь: проверка роли администратора.</param>
    /// <param name="logger">Журнал: только идентификатор пользователя и новое состояние.</param>
    public BlockUserAvatarHandler(
        IUserRepository repository,
        ICurrentUser currentUser,
        ILogger<BlockUserAvatarHandler> logger)
    {
        _repository = repository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Скрывает аватар пользователя и сохраняет агрегат. Повторное скрытие идемпотентно.
    /// </summary>
    /// <param name="command">Пользователь, чей аватар скрывается.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<UnitResult<Failure>> Handle(
        BlockUserAvatarCommand command,
        CancellationToken cancellationToken)
    {
        var allowed = _currentUser.EnsureAdmin();
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var userResult = await _repository.GetByIdAsync(command.UserId, cancellationToken);
        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        var user = userResult.Value;

        var blocked = user.BlockAvatar();
        if (blocked.IsFailure)
        {
            return blocked.Error;
        }

        var saved = await _repository.UpdateAsync(user, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        // Email, URL, путь и имя файла в лог не попадают: только идентификатор и состояние.
        _logger.LogInformation(
            "Avatar of user {UserId} has been blocked by an administrator.",
            user.Id);

        return UnitResult.Success<Failure>();
    }
}
