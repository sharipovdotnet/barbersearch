using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.Admin.UnblockUserAvatar;

/// <summary>
/// Обработчик команды <see cref="UnblockUserAvatarCommand"/>.
/// </summary>
/// <remarks>
/// Требует администратора до чтения: политика <c>AdminOnly</c> на эндпоинте — не единственный
/// вход, команду можно вызвать напрямую. Ошибки репозитория возвращаются как есть
/// (<c>404 user.notFound</c>). Строка аудита <c>user.updated</c> появляется в том же
/// <c>SaveChanges</c>, что и изменение флага, поэтому отдельного вызова аудита здесь нет.
/// </remarks>
public sealed class UnblockUserAvatarHandler
    : ICommandHandler<UnblockUserAvatarCommand>
{
    private readonly IUserRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<UnblockUserAvatarHandler> _logger;

    /// <summary>
    /// Создаёт обработчик.
    /// </summary>
    /// <param name="repository">Пользователь: загрузка агрегата и сохранение.</param>
    /// <param name="currentUser">Текущий пользователь: проверка роли администратора.</param>
    /// <param name="logger">Журнал: только идентификатор пользователя и новое состояние.</param>
    public UnblockUserAvatarHandler(
        IUserRepository repository,
        ICurrentUser currentUser,
        ILogger<UnblockUserAvatarHandler> logger)
    {
        _repository = repository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Возвращает аватар и сохраняет агрегат. Повторный возврат идемпотентен.
    /// </summary>
    /// <param name="command">Пользователь, чей аватар возвращается.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<UnitResult<Failure>> Handle(
        UnblockUserAvatarCommand command,
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

        var unblocked = user.UnblockAvatar();
        if (unblocked.IsFailure)
        {
            return unblocked.Error;
        }

        var saved = await _repository.UpdateAsync(user, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        // Email, URL, путь и имя файла в лог не попадают: только идентификатор и состояние.
        _logger.LogInformation(
            "Avatar of user {UserId} has been unblocked by an administrator.",
            user.Id);

        return UnitResult.Success<Failure>();
    }
}
