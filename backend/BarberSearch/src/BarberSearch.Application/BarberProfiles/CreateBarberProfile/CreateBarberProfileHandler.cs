using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.BarberProfiles;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.CreateBarberProfile;

/// <summary>
/// Обработчик команды <see cref="CreateBarberProfileCommand"/>.
/// </summary>
/// <remarks>
/// Вся проверка данных — доменная: агрегат создаётся фабрикой
/// <see cref="BarberProfile.Create"/>, поэтому дополнительных правил здесь нет.
/// </remarks>
public class CreateBarberProfileHandler : ICommandHandler<Guid, CreateBarberProfileCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ILogger<CreateBarberProfileHandler> _logger;

    public CreateBarberProfileHandler(
        IBarberProfileRepository repository,
        ILogger<CreateBarberProfileHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт профиль барбера и возвращает его идентификатор.
    /// </summary>
    public async Task<Result<Guid, Failure>> Handle(
        CreateBarberProfileCommand command,
        CancellationToken cancellationToken)
    {
        var request = command.Request;

        var entity = BarberProfile.Create(
            userId: request.UserId,
            displayName: request.DisplayName,
            yearsOfExperience: request.YearsOfExperience,
            bio: request.Bio,
            languages: request.Languages,
            certificatesAndTraining: request.CertificatesAndTraining,
            achievements: request.Achievements,
            paymentMethods: request.PaymentMethods,
            communicationFormat: request.CommunicationFormat,
            goodies: request.Goodies,
            latePolicy: request.LatePolicy);
        if (entity.IsFailure)
        {
            return entity.Error;
        }

        var added = await _repository.AddAsync(entity.Value, cancellationToken);
        if (added.IsFailure)
        {
            return added.Error;
        }

        _logger.LogInformation("Barber profile {BarberProfileId} has been created.", entity.Value.Id);

        return entity.Value.Id;
    }
}
