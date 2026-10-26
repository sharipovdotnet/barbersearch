using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Application.Specializations;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.AddBarberProfileSpecialization;

/// <summary>
/// Обработчик команды <see cref="AddBarberProfileSpecializationCommand"/>.
/// </summary>
public class AddBarberProfileSpecializationHandler
    : ICommandHandler<AddBarberProfileSpecializationCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ISpecializationRepository _specializations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AddBarberProfileSpecializationHandler> _logger;

    public AddBarberProfileSpecializationHandler(
        IBarberProfileRepository repository,
        ISpecializationRepository specializations,
        IUnitOfWork unitOfWork,
        ILogger<AddBarberProfileSpecializationHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _specializations = specializations;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Загружает профиль со специализациями, добавляет специализацию и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        AddBarberProfileSpecializationCommand command,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetWithSpecializationsAsync(
            command.BarberProfileId,
            cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;

        var allowed = _currentUser.EnsureCanManage(profile.UserId);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        // Ссылка на справочник специализаций проверяется до вставки, в той же транзакции.
        // Внешний ключ fk_barber_specializations_specializations_specialization_id — последняя линия.
        var result = await _unitOfWork.ExecuteAsync(async ct =>
        {
            var specializationExists = await _specializations.ExistsAsync(
                command.SpecializationId,
                ct);
            if (specializationExists.IsFailure)
            {
                return specializationExists.Error;
            }

            if (!specializationExists.Value)
            {
                return new Failure(Error.NotFound(
                    "barberProfile.addSpecialization.specializationNotFound",
                    $"Specialization '{command.SpecializationId}' was not found.",
                    command.SpecializationId));
            }

            var added = profile.AddSpecialization(command.SpecializationId);
            if (added.IsFailure)
            {
                return added.Error;
            }

            return await _repository.UpdateAsync(profile, ct);
        }, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        _logger.LogInformation(
            "Specialization {SpecializationId} has been added to barber profile {BarberProfileId}.",
            command.SpecializationId,
            profile.Id);

        return UnitResult.Success<Failure>();
    }
}
