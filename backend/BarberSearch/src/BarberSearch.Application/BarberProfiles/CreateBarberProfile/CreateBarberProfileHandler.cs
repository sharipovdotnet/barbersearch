using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Application.Users;
using BarberSearch.Contracts.Common;
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
/// <para>
/// Профиль и привязка к пользователю (<c>User.LinkBarberProfile</c>)
/// пишутся в одной транзакции (R-26): при <c>Failure</c> любого шага не остаётся ни профиля,
/// ни привязки. Второй путь к тому же состоянию — административный
/// <c>PUT api/users/{id}/barber-profile/{id}</c> — остаётся для ручной привязки, но обычное
/// создание профиля больше не требует его вызова: связь в БД заполняется сразу, а claim
/// <c>barber_profile_id</c> появляется в следующем выданном токене.
/// </para>
/// </remarks>
public class CreateBarberProfileHandler : ICommandHandler<Guid, CreateBarberProfileCommand>
{
    /// <summary>
    /// Текст ошибки «у текущего пользователя уже есть профиль барбера».
    /// </summary>
    private const string ALREADY_EXISTS_MESSAGE =
        "The current user already has a barber profile.";

    private readonly IBarberProfileRepository _repository;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CreateBarberProfileHandler> _logger;

    public CreateBarberProfileHandler(
        IBarberProfileRepository repository,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ILogger<CreateBarberProfileHandler> logger)
    {
        _repository = repository;
        _users = users;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт профиль барбера, привязывает его к текущему пользователю и возвращает
    /// идентификатор профиля.
    /// </summary>
    public async Task<Result<Guid, Failure>> Handle(
        CreateBarberProfileCommand command,
        CancellationToken cancellationToken)
    {
        var request = command.Request;

        // Аноним получает 401 auth.unauthorized, а не Guid.Empty (R-29).
        var userIdResult = _currentUser.RequireUserId();
        if (userIdResult.IsFailure)
        {
            return userIdResult.Error;
        }

        // Сначала доменная валидация, потом запись: невалидный запрос не открывает транзакцию
        // и не делает ни одного запроса в репозитории.
        var entity = BarberProfile.Create(
            userId: userIdResult.Value,
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

        var profile = entity.Value;

        // Профиль и пользователь — два агрегата, поэтому запись идёт в одной транзакции (R-26).
        // Пользователь читается внутри неё: проверка «профиль уже есть» и UPDATE привязки
        // должны видеть одно и то же состояние.
        var result = await _unitOfWork.ExecuteAsync<Guid>(async token =>
        {
            var userResult = await _users.GetByIdAsync(userIdResult.Value, token);
            if (userResult.IsFailure)
            {
                return userResult.Error;
            }

            var user = userResult.Value;

            // 409 вместо unique-индекса ux_barber_profiles_user_id: понятный код и никаких
            // записей, которые пришлось бы откатывать из-за нарушения ограничения БД.
            if (user.BarberProfileId is not null)
            {
                return new Failure(Error.Conflict(
                    ApiErrorCodes.BarberProfile.CreateAlreadyExists,
                    ALREADY_EXISTS_MESSAGE));
            }

            var added = await _repository.AddAsync(profile, token);
            if (added.IsFailure)
            {
                // Та же гонка, что и предполётная проверка, но обнаруженная уникальным
                // индексом ux_barber_profiles_user_id: клиент должен увидеть один и тот же
                // 409 barberProfile.create.alreadyExists, а не инфраструктурный код.
                if (added.Error.Errors.Any(error =>
                        error.Code == ApiErrorCodes.BarberProfile.AddUniqueViolation))
                {
                    return new Failure(Error.Conflict(
                        ApiErrorCodes.BarberProfile.CreateAlreadyExists,
                        ALREADY_EXISTS_MESSAGE));
                }

                return added.Error;
            }

            var linked = user.LinkBarberProfile(profile.Id);
            if (linked.IsFailure)
            {
                return linked.Error;
            }

            var saved = await _users.UpdateAsync(user, token);
            if (saved.IsFailure)
            {
                return saved.Error;
            }

            return profile.Id;
        }, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Barber profile {BarberProfileId} has been created and linked to user {UserId}.",
                profile.Id,
                userIdResult.Value);
        }

        return result;
    }
}
