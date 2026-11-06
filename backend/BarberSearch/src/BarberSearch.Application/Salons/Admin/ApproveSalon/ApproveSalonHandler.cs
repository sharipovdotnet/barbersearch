using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Salons;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Salons.Admin.ApproveSalon;

/// <summary>
/// Обработчик команды <see cref="ApproveSalonCommand"/>.
/// </summary>
/// <remarks>
/// Доступ проверяет контроллер политикой <c>AdminOnly</c>. Решение фиксирует администратора
/// и момент времени; салон становится активным и видимым. Повторное решение — <c>409
/// salon.review.invalidStatus</c>, его возвращает домен, а не отдельная предпроверка.
/// </remarks>
public class ApproveSalonHandler : ICommandHandler<ApproveSalonCommand>
{
    private readonly ISalonRepository _salons;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ApproveSalonHandler> _logger;

    public ApproveSalonHandler(
        ISalonRepository salons,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<ApproveSalonHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(salons);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _salons = salons;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Одобряет салон и сохраняет решение вместе с аудитом <c>salon.updated</c>.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        ApproveSalonCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var adminIdResult = _currentUser.RequireUserId();
        if (adminIdResult.IsFailure)
        {
            return adminIdResult.Error;
        }

        var adminId = adminIdResult.Value;
        var now = _timeProvider.GetUtcNow();

        var result = await _unitOfWork.ExecuteAsync(async token =>
        {
            var salonResult = await _salons.GetByIdAsync(command.SalonId, token);
            if (salonResult.IsFailure)
            {
                return salonResult.Error;
            }

            var salon = salonResult.Value;

            var approved = salon.Approve(adminId, now);
            if (approved.IsFailure)
            {
                return approved.Error;
            }

            return await _salons.UpdateAsync(salon, token);
        }, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        _logger.LogInformation(
            "Salon {SalonId} has been approved by administrator {AdminId}.",
            command.SalonId,
            adminId);

        return UnitResult.Success<Failure>();
    }
}
