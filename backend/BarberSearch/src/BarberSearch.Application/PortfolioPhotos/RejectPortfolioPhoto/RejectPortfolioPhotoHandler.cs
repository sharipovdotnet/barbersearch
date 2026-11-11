using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.PortfolioPhotos.RejectPortfolioPhoto;

/// <summary>
/// Обработчик команды <see cref="RejectPortfolioPhotoCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>EnsureAdmin</c> вызывается до чтения агрегата: политика <c>AdminOnly</c> на эндпоинте —
/// не единственный вход. Аноним получает <c>401 auth.unauthorized</c>, пользователь без роли —
/// <c>403 auth.forbidden</c>.
/// </para>
/// <para>
/// Фотография ищется в коллекции загруженного профиля, поэтому «чужая» фотография не
/// отклоняется. Причина — текст администратора: она сохраняется на кадре и показывается
/// владельцу, но в журнал не пишется. Отклонение и запись идут в одной транзакции
/// <c>IUnitOfWork</c>, ошибки домена возвращаются как есть.
/// </para>
/// </remarks>
public sealed class RejectPortfolioPhotoHandler
    : ICommandHandler<RejectPortfolioPhotoCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RejectPortfolioPhotoHandler> _logger;

    /// <summary>
    /// Создаёт обработчик.
    /// </summary>
    /// <param name="repository">Профили барберов: загрузка агрегата вместе с портфолио.</param>
    /// <param name="unitOfWork">Транзакция решения и записи.</param>
    /// <param name="currentUser">Текущий пользователь: проверка роли администратора.</param>
    /// <param name="timeProvider">Часы: момент решения <c>ReviewedAt</c>.</param>
    /// <param name="logger">Журнал: только идентификаторы и решение.</param>
    public RejectPortfolioPhotoHandler(
        IBarberProfileRepository repository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<RejectPortfolioPhotoHandler> logger)
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
    /// Отклоняет фотографию с причиной и сохраняет агрегат вместе с решением администратора.
    /// </summary>
    /// <param name="command">Профиль, фотография и причина отказа.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<UnitResult<Failure>> Handle(
        RejectPortfolioPhotoCommand command,
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
            var profileResult = await _repository.GetWithPhotosAsync(
                command.BarberProfileId,
                token);
            if (profileResult.IsFailure)
            {
                return profileResult.Error;
            }

            var profile = profileResult.Value;

            var photo = profile.PortfolioPhotos.FirstOrDefault(p => p.Id == command.PhotoId);
            if (photo is null)
            {
                return new Failure(Error.NotFound(
                    ApiErrorCodes.PortfolioPhoto.NotFound,
                    DomainMessages.PHOTO_NOT_FOUND,
                    command.PhotoId));
            }

            // Статус и причина проверяются доменом до записи; транзакция откатится,
            // и кадр останется в прежнем состоянии.
            var rejected = photo.Reject(adminId, command.Reason, now);
            if (rejected.IsFailure)
            {
                return rejected.Error;
            }

            return await _repository.UpdateAsync(profile, token);
        }, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        // Причина отказа — текст администратора; URL и подпись тоже не логируются.
        _logger.LogInformation(
            "Portfolio photo {PhotoId} of barber profile {BarberProfileId} has been rejected by administrator {AdminId}.",
            command.PhotoId,
            command.BarberProfileId,
            adminId);

        return UnitResult.Success<Failure>();
    }
}
