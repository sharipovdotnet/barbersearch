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
/// Профиль всегда создаётся для текущего пользователя: идентификатор берётся из токена,
/// а роль <c>Barber</c> проверяет политика <c>BarberOnly</c> на эндпоинте.
/// </remarks>
public class CreateBarberProfileHandler : ICommandHandler<Guid, CreateBarberProfileCommand>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CreateBarberProfileHandler> _logger;

    public CreateBarberProfileHandler(
        IBarberProfileRepository repository,
        ICurrentUser currentUser,
        ILogger<CreateBarberProfileHandler> logger)
    {
        _repository = repository;
        _currentUser = currentUser;
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
            userId: _currentUser.UserId,
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
