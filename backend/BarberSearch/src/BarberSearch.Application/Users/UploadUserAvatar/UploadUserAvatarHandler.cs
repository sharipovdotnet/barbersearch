using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Application.PortfolioPhotos;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.UploadUserAvatar;

/// <summary>
/// Обработчик команды <see cref="UploadUserAvatarCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// Порядок шагов важен: <c>RequireUserId</c> и загрузка пользователя идут до чтения файла,
/// файл проверяется до записи, а сохранение файла — до <c>SaveChanges</c>. Любая ошибка после
/// записи файла (домен или БД) удаляет <b>новый</b> файл компенсацией: строка в БД и файл на
/// диске не должны расходиться. Предыдущий аватар не трогается, пока запись в БД не удалась.
/// </para>
/// <para>
/// Лимиты и формат — тот же <see cref="ImageFileInspector"/>, что у фотографий портфолио:
/// 1 КиБ..5 МиБ, 300..6000 px, не больше 24 Мп, JPEG/PNG/WebP по сигнатуре. Второго
/// «аватарного» набора проверок нет: одна проверка — один источник правды, а квадрат 512×512
/// готовит браузер, серверного кропа в срезе нет.
/// </para>
/// <para>
/// Гонка двух параллельных загрузок: побеждает последняя запись, файл проигравшей может
/// остаться сиротой — это допустимо, версии строки у <see cref="User"/> нет.
/// </para>
/// <para>
/// Премодерация: <c>SetAvatar</c> переводит новый кадр в <c>Pending</c>, поэтому ответ
/// содержит и URL, и состояние проверки. Публично кадр не виден до одобрения
/// администратором; в кабинете владелец видит превью с бейджем «На проверке».
/// </para>
/// </remarks>
public sealed class UploadUserAvatarHandler
    : ICommandHandler<AvatarUploadResponse, UploadUserAvatarCommand>
{
    private readonly IUserRepository _repository;
    private readonly IPhotoStorage _storage;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<UploadUserAvatarHandler> _logger;

    /// <summary>
    /// Создаёт обработчик.
    /// </summary>
    /// <param name="repository">Пользователь: загрузка агрегата и сохранение.</param>
    /// <param name="storage">Хранилище файлов: запись нового и удаление старого.</param>
    /// <param name="currentUser">Идентификатор текущего пользователя из claim'а <c>sub</c>.</param>
    /// <param name="logger">Журнал: только идентификатор, размер и габариты.</param>
    public UploadUserAvatarHandler(
        IUserRepository repository,
        IPhotoStorage storage,
        ICurrentUser currentUser,
        ILogger<UploadUserAvatarHandler> logger)
    {
        _repository = repository;
        _storage = storage;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Проверяет файл, сохраняет его, ставит URL аватару и возвращает URL со статусом проверки.
    /// </summary>
    /// <param name="command">Команда с содержимым файла.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<Result<AvatarUploadResponse, Failure>> Handle(
        UploadUserAvatarCommand command,
        CancellationToken cancellationToken)
    {
        // Аноним не должен даже читать файл: 401 приходит до обращения к хранилищу.
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

        var inspection = ImageFileInspector.Inspect(command.Content.Span);
        if (inspection.IsFailure)
        {
            return inspection.Error;
        }

        var info = inspection.Value;

        // Предыдущий URL запоминается до записи: после успешного SaveChanges его файл
        // удаляется best-effort, но только если это собственный media-URL.
        var previousUrl = user.AvatarUrl;
        var mediaId = Guid.CreateVersion7();

        var stored = await _storage.SaveAvatarAsync(
            user.Id,
            mediaId,
            info.Extension,
            command.Content,
            cancellationToken);
        if (stored.IsFailure)
        {
            return stored.Error;
        }

        var storedUrl = stored.Value.Url;

        var applied = user.SetAvatar(storedUrl);
        if (applied.IsFailure)
        {
            await DeleteBestEffortAsync(user.Id, storedUrl, cancellationToken);

            return applied.Error;
        }

        var saved = await _repository.UpdateAsync(user, cancellationToken);
        if (saved.IsFailure)
        {
            // Предыдущий аватар остаётся в БД и на диске: строка не сохранилась.
            await DeleteBestEffortAsync(user.Id, storedUrl, cancellationToken);

            return saved.Error;
        }

        // Строка сохранена — только теперь можно убирать старый файл. Внешняя ссылка
        // (легаси-путь администратора) не наша: её не удаляем и в хранилище не идём.
        if (IsOwnAvatarMediaUrl(previousUrl, user.Id))
        {
            await DeleteBestEffortAsync(user.Id, previousUrl!, cancellationToken);
        }

        // Логируются только идентификатор, размер и габариты: ни URL, ни пути, ни имени файла.
        _logger.LogInformation(
            "Avatar of user {UserId} has been uploaded: {Size} bytes, {Width}x{Height}.",
            user.Id,
            command.Content.Length,
            info.Width,
            info.Height);

        // Состояние берётся у агрегата, а не подставляется константой: SetAvatar — источник
        // правды о премодерации (сегодня это всегда Pending).
        return new AvatarUploadResponse(storedUrl, user.AvatarModerationStatus);
    }

    /// <summary>
    /// Удаляет файл, оставшийся без строки в БД. Ошибка удаления не подменяет исходную:
    /// файл-сирота не ломает UX, поэтому в ответ уходит исходный <see cref="Failure"/>.
    /// </summary>
    private async Task DeleteBestEffortAsync(
        Guid userId,
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            await _storage.DeleteAsync(url, cancellationToken);
        }
        catch (Exception exception)
        {
            // URL и путь к файлу в лог не попадают: только идентификатор пользователя.
            _logger.LogWarning(
                exception,
                "Failed to delete an avatar file of user {UserId}.",
                userId);
        }
    }

    /// <summary>
    /// Является ли URL собственным media-URL аватара этого пользователя.
    /// </summary>
    private static bool IsOwnAvatarMediaUrl(string? url, Guid userId)
        => url is not null && UserAvatarMediaUrl.TryParse(url, userId, out _, out _);
}
