using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Application.PortfolioPhotos;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.DeleteUserAvatar;

/// <summary>
/// Обработчик команды <see cref="DeleteUserAvatarCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// Операция идемпотентна: пользователь без аватара получает успех без обращения к хранилищу,
/// повторное удаление — тоже успех. Если аватар есть, сначала запоминается URL, затем
/// <see cref="User.SetAvatar"/> с <c>null</c> и <c>UpdateAsync</c>; файл удаляется
/// best-effort только после успешной записи в БД — при ошибке сохранения аватар остаётся
/// и в строке, и на диске.
/// </para>
/// <para>
/// Удаляется только собственный media-URL аватара. Внешняя абсолютная ссылка
/// (легаси-путь администратора) на диск не проецируется, поэтому хранилище для неё
/// не вызывается.
/// </para>
/// </remarks>
public sealed class DeleteUserAvatarHandler
    : ICommandHandler<DeleteUserAvatarCommand>
{
    private readonly IUserRepository _repository;
    private readonly IPhotoStorage _storage;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<DeleteUserAvatarHandler> _logger;

    /// <summary>
    /// Создаёт обработчик.
    /// </summary>
    /// <param name="repository">Пользователь: загрузка агрегата и сохранение.</param>
    /// <param name="storage">Хранилище файлов: удаление прежнего аватара.</param>
    /// <param name="currentUser">Идентификатор текущего пользователя из claim'а <c>sub</c>.</param>
    /// <param name="logger">Журнал: только идентификатор пользователя.</param>
    public DeleteUserAvatarHandler(
        IUserRepository repository,
        IPhotoStorage storage,
        ICurrentUser currentUser,
        ILogger<DeleteUserAvatarHandler> logger)
    {
        _repository = repository;
        _storage = storage;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Снимает аватар и удаляет его файл, если он был.
    /// </summary>
    /// <param name="command">Команда без параметров.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<UnitResult<Failure>> Handle(
        DeleteUserAvatarCommand command,
        CancellationToken cancellationToken)
    {
        var userIdResult = _currentUser.RequireUserId();
        if (userIdResult.IsFailure)
        {
            return userIdResult.Error;
        }

        var userResult = await _repository.GetByIdAsync(userIdResult.Value, cancellationToken);
        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        var user = userResult.Value;

        // Аватара нет — успех без записи в БД и без обращения к диску.
        if (user.AvatarUrl is null)
        {
            return UnitResult.Success<Failure>();
        }

        var previousUrl = user.AvatarUrl;

        var cleared = user.SetAvatar(null);
        if (cleared.IsFailure)
        {
            return cleared.Error;
        }

        var saved = await _repository.UpdateAsync(user, cancellationToken);
        if (saved.IsFailure)
        {
            // Строка не сохранилась — файл остаётся: аватар по-прежнему действующий.
            return saved.Error;
        }

        if (IsOwnAvatarMediaUrl(previousUrl, user.Id))
        {
            try
            {
                await _storage.DeleteAsync(previousUrl, cancellationToken);
            }
            catch (Exception exception)
            {
                // Файл-сирота не ломает UX, а URL в лог не попадает.
                _logger.LogWarning(
                    exception,
                    "Failed to delete an avatar file of user {UserId}.",
                    user.Id);
            }
        }

        _logger.LogInformation("Avatar of user {UserId} has been deleted.", user.Id);

        return UnitResult.Success<Failure>();
    }

    /// <summary>
    /// Является ли URL собственным media-URL аватара этого пользователя.
    /// </summary>
    private static bool IsOwnAvatarMediaUrl(string? url, Guid userId)
        => url is not null && UserAvatarMediaUrl.TryParse(url, userId, out _, out _);
}
