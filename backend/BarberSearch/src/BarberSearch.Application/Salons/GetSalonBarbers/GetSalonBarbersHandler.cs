using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Salons.GetSalonBarbers;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Salons.GetSalonBarbers;

/// <summary>
/// Обработчик запроса <see cref="GetSalonBarbersQuery"/>.
/// </summary>
/// <remarks>
/// Салон загружается ради проверки владения: <c>EnsureCanManageSalon</c> сравнивает
/// <c>Salon.OwnerUserId</c> с текущим пользователем, поэтому чужой салон получает
/// <c>403 auth.forbidden</c>, а анонимный запрос — <c>401 auth.unauthorized</c>.
/// Только после проверки читается список профилей.
/// </remarks>
public sealed class GetSalonBarbersHandler
    : IQueryHandler<IReadOnlyList<SalonBarberResponse>, GetSalonBarbersQuery>
{
    private readonly ISalonRepository _salons;
    private readonly IBarberProfileRepository _profiles;
    private readonly ICurrentUser _currentUser;

    public GetSalonBarbersHandler(
        ISalonRepository salons,
        IBarberProfileRepository profiles,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(salons);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(currentUser);

        _salons = salons;
        _profiles = profiles;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<SalonBarberResponse>, Failure>> Handle(
        GetSalonBarbersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var salonResult = await _salons.GetByIdAsync(query.SalonId, cancellationToken);
        if (salonResult.IsFailure)
        {
            return salonResult.Error;
        }

        var allowed = _currentUser.EnsureCanManageSalon(salonResult.Value);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        return await _profiles.GetSalonBarbersAsync(query.SalonId, cancellationToken);
    }
}
