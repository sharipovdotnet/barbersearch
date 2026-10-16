using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.BarberSalons;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberProfiles.FindBarberProfileSalon;

/// <summary>
/// Обработчик запроса <see cref="FindBarberProfileSalonQuery"/>.
/// </summary>
/// <remarks>
/// Агрегат только читается: <c>UpdateAsync</c> здесь не вызывается, потому что запрос
/// не меняет состояние профиля.
/// </remarks>
public class FindBarberProfileSalonHandler
    : IQueryHandler<BarberSalon, FindBarberProfileSalonQuery>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ILogger<FindBarberProfileSalonHandler> _logger;

    public FindBarberProfileSalonHandler(
        IBarberProfileRepository repository,
        ILogger<FindBarberProfileSalonHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает профиль барбера и возвращает салон по идентификатору салона.
    /// </summary>
    public async Task<Result<BarberSalon, Failure>> Handle(
        FindBarberProfileSalonQuery query,
        CancellationToken cancellationToken)
    {
        var profileResult = await _repository.GetByIdAsync(query.BarberProfileId, cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        var salonResult = profileResult.Value.FindSalon(query.SalonId);
        if (salonResult.IsFailure)
        {
            return salonResult.Error;
        }

        _logger.LogInformation(
            "Salon {SalonId} has been found in barber profile {BarberProfileId}.",
            query.SalonId,
            query.BarberProfileId);

        return salonResult.Value;
    }
}
