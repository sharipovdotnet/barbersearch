using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberProfiles.GetBarberBookingOptions;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.BarberProfiles.GetBarberBookingOptions;

/// <summary>
/// Обработчик чтения услуг и салонов для формы «Записать клиента».
/// </summary>
/// <remarks>
/// Единственная проверка здесь — непустой идентификатор профиля: владение и наличие
/// профиля подтверждает claim сессии, а коллекции читает репозиторий. Пустой id —
/// <c>400 barberProfile.id.invalid</c>, как у остальных чтений профиля.
/// </remarks>
public sealed class GetBarberBookingOptionsHandler
    : IQueryHandler<BarberBookingOptionsResponse, GetBarberBookingOptionsQuery>
{
    private readonly IBarberProfileRepository _profiles;

    public GetBarberBookingOptionsHandler(IBarberProfileRepository profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        _profiles = profiles;
    }

    /// <summary>
    /// Возвращает активные услуги и салоны профиля либо <c>404</c>, если профиля нет.
    /// </summary>
    public async Task<Result<BarberBookingOptionsResponse, Failure>> Handle(
        GetBarberBookingOptionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.BarberProfileId == Guid.Empty)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.BarberProfile.InvalidId,
                "A barber profile identifier is required.",
                nameof(query.BarberProfileId)));
        }

        return await _profiles.GetBookingOptionsAsync(
            query.BarberProfileId,
            cancellationToken);
    }
}
