using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Salons;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Salons.Admin.RejectSalon;

/// <summary>
/// Обработчик команды <see cref="RejectSalonCommand"/>.
/// </summary>
/// <remarks>
/// Доступ проверяет контроллер политикой <c>AdminOnly</c>. Причина отказа валидируется
/// доменом; салон остаётся невидимым, а причина показывается владельцу в «Моих салонах».
/// Повторное решение — <c>409 salon.review.invalidStatus</c>.
/// </remarks>
public class RejectSalonHandler : ICommandHandler<RejectSalonCommand>
{
    private readonly ISalonRepository _salons;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RejectSalonHandler> _logger;

    public RejectSalonHandler(
        ISalonRepository salons,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<RejectSalonHandler> logger)
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
    /// Отклоняет салон, сохраняет причину и решение администратора.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        RejectSalonCommand command,
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

            var rejected = salon.Reject(adminId, command.Reason, now);
            if (rejected.IsFailure)
            {
                return rejected.Error;
            }

            return await _salons.UpdateAsync(salon, token);
        }, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        _logger.LogInformation(
            "Salon {SalonId} has been rejected by administrator {AdminId}.",
            command.SalonId,
            adminId);

        return UnitResult.Success<Failure>();
    }
}
