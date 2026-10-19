using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
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
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<FindBarberProfileSalonHandler> _logger;

    public FindBarberProfileSalonHandler(
        IBarberProfileRepository repository,
        ILogger<FindBarberProfileSalonHandler> logger,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _logger = logger;
        _currentUser = currentUser;
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

        var profile = profileResult.Value;

        var allowed = _currentUser.EnsureCanManage(profile.UserId);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var salonResult = profile.FindSalon(query.SalonId);
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
