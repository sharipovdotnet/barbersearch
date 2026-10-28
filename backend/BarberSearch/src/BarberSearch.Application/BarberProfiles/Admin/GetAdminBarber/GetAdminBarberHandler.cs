using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.BarberProfiles.Admin.GetAdminBarber;

/// <summary>
/// Обработчик карточки барбера для админского экрана.
/// </summary>
/// <remarks>
/// Возвращает профиль любого статуса: администратор видит и неподтверждённые, и скрытые
/// профили. Публичная карточка <c>GET api/barbers/{id}</c> устроена иначе и такие профили
/// отдаёт как <c>404</c>.
/// </remarks>
public sealed class GetAdminBarberHandler
    : IQueryHandler<AdminBarberDetailResponse, GetAdminBarberQuery>
{
    private readonly IBarberProfileRepository _barberProfileRepository;

    public GetAdminBarberHandler(IBarberProfileRepository barberProfileRepository)
    {
        ArgumentNullException.ThrowIfNull(barberProfileRepository);

        _barberProfileRepository = barberProfileRepository;
    }

    /// <summary>
    /// Возвращает барбера любого статуса либо <c>404 barberProfile.notFound</c>.
    /// </summary>
    public async Task<Result<AdminBarberDetailResponse, Failure>> Handle(
        GetAdminBarberQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.BarberProfileId == Guid.Empty)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AdminBarber.InvalidId,
                "Barber profile id must not be empty.",
                nameof(query.BarberProfileId)));
        }

        return await _barberProfileRepository.GetAdminByIdAsync(
            query.BarberProfileId,
            cancellationToken);
    }
}
