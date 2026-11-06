using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Salons;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Salons.CreateOwnSalon;

/// <summary>
/// Обработчик команды <see cref="CreateOwnSalonCommand"/>.
/// </summary>
/// <remarks>
/// Путь барбера: салон создаётся в статусе <see cref="SalonModerationStatus.Pending"/>
/// и сразу привязывается к профилю барбера в одной транзакции. Профиль берётся из claim'а
/// <c>barber_profile_id</c>, а не из тела запроса: иначе барбер мог бы привязать салон
/// к чужому профилю.
/// <para>
/// Доступ проверяет контроллер политикой <c>BarberOnly</c>. Отдельно проверяется наличие
/// профиля: барбер без профиля получает <c>403 barberProfile.notFound</c>, а не 500 на
/// отсутствующем claim'е.
/// </para>
/// </remarks>
public class CreateOwnSalonHandler : ICommandHandler<Guid, CreateOwnSalonCommand>
{
    /// <summary>
    /// Текст ошибки «у пользователя нет профиля барбера».
    /// </summary>
    private const string NO_PROFILE_MESSAGE =
        "A barber profile is required to create a salon.";

    private readonly ISalonRepository _salons;
    private readonly IBarberProfileRepository _profiles;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CreateOwnSalonHandler> _logger;

    public CreateOwnSalonHandler(
        ISalonRepository salons,
        IBarberProfileRepository profiles,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ILogger<CreateOwnSalonHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(salons);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(logger);

        _salons = salons;
        _profiles = profiles;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт салон на модерации, привязывает его к профилю и возвращает идентификатор.
    /// </summary>
    public async Task<Result<Guid, Failure>> Handle(
        CreateOwnSalonCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!_currentUser.TryGetBarberProfileId(out var barberProfileId))
        {
            return new Failure(Error.Forbidden(
                ApiErrorCodes.BarberProfile.NotFound,
                NO_PROFILE_MESSAGE));
        }

        var request = command.Request;

        // Салон и его привязка к профилю — атомарно: без транзакции барбер мог бы получить
        // салон, которого нет в «Моих салонах», или привязку к несохранённому салону.
        var result = await _unitOfWork.ExecuteAsync<Guid>(async token =>
        {
            var profileResult = await _profiles.GetWithSalonsAsync(barberProfileId, token);
            if (profileResult.IsFailure)
            {
                return profileResult.Error;
            }

            var profile = profileResult.Value;

            var salonResult = Salon.SubmitByOwner(
                profile.UserId,
                request.Name,
                request.Address,
                request.City,
                request.Country,
                request.TimeZoneId,
                request.Latitude,
                request.Longitude,
                request.PostalCode,
                request.PhoneNumber,
                request.WebsiteUrl);
            if (salonResult.IsFailure)
            {
                return salonResult.Error;
            }

            var salon = salonResult.Value;

            var added = await _salons.AddAsync(salon, token);
            if (added.IsFailure)
            {
                return added.Error;
            }

            // Первый салон барбера становится основным: расписание и слоты считаются
            // по паре (барбер, салон), и основной салон нужен форме записи.
            var linked = profile.AddSalon(salon.Id, isPrimary: profile.Salons.Count == 0);
            if (linked.IsFailure)
            {
                return linked.Error;
            }

            var saved = await _profiles.UpdateAsync(profile, token);
            if (saved.IsFailure)
            {
                return saved.Error;
            }

            return salon.Id;
        }, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error;
        }

        _logger.LogInformation(
            "Salon {SalonId} has been submitted by barber {BarberProfileId} for moderation.",
            result.Value,
            barberProfileId);

        return result.Value;
    }
}
