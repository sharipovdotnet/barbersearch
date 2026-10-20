using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.BarberProfiles.GetBarberProfile;

/// <summary>
/// Обработчик запроса карточки барбера.
/// </summary>
/// <remarks>
/// Карточка читается только для активного и подтверждённого профиля; иначе профиль
/// считается отсутствующим и возвращается 404. Репозиторий отдаёт готовую projection
/// в <see cref="BarberProfileResponse"/> — доменный граф с навигациями наружу не выходит.
/// </remarks>
public sealed class GetBarberProfileHandler
    : IQueryHandler<BarberProfileResponse, GetBarberProfileQuery>
{
    private readonly IBarberProfileRepository _repository;

    public GetBarberProfileHandler(IBarberProfileRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _repository = repository;
    }

    /// <summary>
    /// Возвращает карточку барбера или 404.
    /// </summary>
    public Task<Result<BarberProfileResponse, Failure>> Handle(
        GetBarberProfileQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.BarberProfileId == Guid.Empty)
        {
            return Task.FromResult<Result<BarberProfileResponse, Failure>>(
                new Failure(Error.Validation(
                    "barberProfile.getById.invalidId",
                    "Barber profile identifier cannot be empty.",
                    nameof(query.BarberProfileId))));
        }

        return _repository.GetCardByIdAsync(query.BarberProfileId, cancellationToken);
    }
}
