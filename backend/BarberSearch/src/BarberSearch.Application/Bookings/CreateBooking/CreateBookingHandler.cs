using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using BarberSearch.Application.Salons;
using BarberSearch.Domain;
using BarberSearch.Domain.Bookings;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Bookings.CreateBooking;

/// <summary>
/// Обработчик команды <see cref="CreateBookingCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// Клиент брони берётся из токена (<see cref="ICurrentUser"/>), а не из тела запроса, поэтому
/// создать бронь от чужого имени нельзя; в рамках этого же пользователя ищется и ключ
/// идемпотентности. Первым шагом ключ нормализуется и проверяется по доменным правилам
/// (<c>booking.create.idempotencyKey</c> / <c>booking.create.idempotencyKey.tooLong</c>):
/// пустой или слишком длинный ключ — это <c>400</c> до обращения к репозиторию (R-09).
/// Если бронь с таким ключом уже есть, возвращается её идентификатор, и вторая бронь не
/// создаётся. Отсутствие записи по ключу (<see cref="ErrorType.NOT_FOUND"/>) — не ошибка
/// сценария, а признак того, что создание нужно продолжить.
/// </para>
/// <para>
/// Цена берётся из <c>BarberService.Price</c>, а не из запроса: клиент не должен назначать
/// стоимость сам. Поля <c>price</c> в запросе больше нет (R-33), поэтому передать свою цену
/// нельзя даже по ошибке. Услуга ищется узким чтением профиля и должна принадлежать именно этому
/// барберу и быть активной; иначе — <c>404</c>.
/// </para>
/// <para>
/// Гонку двух одновременных вставок разрешает PostgreSQL: exclusion-ограничение
/// <c>ex_bookings_no_overlap</c> отдаёт <c>23P01</c>, unique-индекс
/// <c>ux_bookings_user_id_idempotency_key</c> — <c>23505</c>. Оба маппятся в <c>Conflict</c>.
/// После такого отказа обработчик ещё раз читает бронь по ключу: если параллельный запрос
/// с тем же ключом уже сохранил её, возвращается его идентификатор (идемпотентный ответ),
/// иначе наружу уходит исходный <c>409</c>.
/// </para>
/// <para>
/// До проверок ссылок обработчик берёт транзакционную advisory-блокировку барбера через
/// <see cref="IBookingRepository.AcquireBarberLockAsync"/>. Транзакцией владеет
/// <see cref="IUnitOfWork"/>: блокировка живёт до коммита, поэтому проверки и вставка
/// сериализованы для одного барбера, а репозиторий не открывает вложенную транзакцию (R-15).
/// Ожидание ограничено <c>lock_timeout</c>: если блокировку не удалось взять, обработчик
/// пишет warning и возвращает <c>409 booking.add.lockTimeout</c>, а не <c>500</c> (R-18).
/// </para>
/// <para>
/// Временное окно проверяется здесь, а не в домене: сравнение с «сейчас» — забота
/// Application (AGENT.md §7.6), а домен знает только верхнюю границу длительности.
/// <c>StartAt</c> обязан попасть в <c>[now − допуск; now + горизонт]</c>: клиент не может
/// забронировать прошлое или бесконечное будущее. Сравнение идёт по абсолютному моменту
/// (<c>DateTimeOffset</c>), поэтому часовой пояс салона его не меняет, а допуск покрывает
/// расхождение часов клиента и сервера. Проверка стоит после ключа идемпотентности: повтор
/// запроса по ключу возвращает бронь, даже если её начало уже прошло.
/// </para>
/// </remarks>
public class CreateBookingHandler : ICommandHandler<Guid, CreateBookingCommand>
{
    private readonly IBookingRepository _repository;
    private readonly IBarberProfileRepository _profiles;
    private readonly ISalonRepository _salons;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CreateBookingHandler> _logger;

    public CreateBookingHandler(
        IBookingRepository repository,
        IBarberProfileRepository profiles,
        ISalonRepository salons,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<CreateBookingHandler> logger)
    {
        _repository = repository;
        _profiles = profiles;
        _salons = salons;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт бронь и возвращает её идентификатор; повтор запроса с тем же ключом
    /// идемпотентности возвращает идентификатор уже существующей брони.
    /// </summary>
    public async Task<Result<Guid, Failure>> Handle(
        CreateBookingCommand command,
        CancellationToken cancellationToken)
    {
        var request = command.Request;

        // Анонимный запрос получает 401 auth.unauthorized, а не исключение из ICurrentUser:
        // инвариант явный, потому что read-эндпоинты могут стать анонимными (R-25, R-29).
        var userIdResult = _currentUser.RequireUserId();
        if (userIdResult.IsFailure)
        {
            return userIdResult.Error;
        }

        var userId = userIdResult.Value;

        // Ключ сравнивается и сохраняется в одном виде: иначе " key " и "key" были бы
        // разными ключами для unique-индекса, хотя клиент считает их одним запросом.
        // Проверка идёт до чтения из репозитория: иначе пустой ключ превращался бы в ошибку
        // инфраструктуры вместо доменного 400 booking.create.idempotencyKey (R-09).
        var idempotencyKey = Booking.NormalizeIdempotencyKey(request.IdempotencyKey);

        if (Booking.ValidateIdempotencyKey(idempotencyKey) is { } idempotencyKeyError)
        {
            return new Failure(idempotencyKeyError);
        }

        var existing = await _repository.GetByIdempotencyKeyAsync(
            userId,
            idempotencyKey,
            cancellationToken);
        if (existing.IsSuccess)
        {
            _logger.LogInformation(
                "Booking {BookingId} has been returned by idempotency key.",
                existing.Value.Id);

            return existing.Value.Id;
        }

        var missing = existing.Error.Errors.Any(error => error.Type == ErrorType.NOT_FOUND);
        if (!missing)
        {
            return existing.Error;
        }

        // Окно допустимости — после ключа идемпотентности: повтор запроса по ключу обязан
        // вернуть уже созданную бронь, даже если её начало успело пройти (R-04).
        var now = _timeProvider.GetUtcNow();
        var earliestStart = now.AddMinutes(-Constants.BOOKING_START_TOLERANCE_MINUTES);

        if (request.StartAt < earliestStart)
        {
            return new Failure(Error.Validation(
                "booking.create.startAt.inPast",
                "A booking cannot start in the past.",
                nameof(request.StartAt)));
        }

        var latestStart = now.AddDays(Constants.BOOKING_HORIZON_DAYS);

        if (request.StartAt > latestStart)
        {
            return new Failure(Error.Validation(
                "booking.create.startAt.tooFar",
                $"A booking cannot start more than {Constants.BOOKING_HORIZON_DAYS} days ahead.",
                nameof(request.StartAt)));
        }

        // Ссылки (услуга, салон и пара «барбер — салон») проверяются и бронь вставляется
        // в одной транзакции: иначе салон можно удалить между EXISTS и INSERT. Для брони
        // внешних ключей нет (история визитов должна переживать удаление справочников),
        // поэтому транзакционная проверка здесь — основная защита (R-02).
        Failure? addFailure = null;

        var created = await _unitOfWork.ExecuteAsync<Guid>(async ct =>
        {
            // Блокировка берётся до проверок ссылок и живёт до коммита транзакции
            // IUnitOfWork: параллельные создания броней этого барбера проходят проверки
            // и вставку по очереди. Транзакцию репозиторий не открывает (R-15).
            // Ожидание ограничено lock_timeout: при 55P03 возвращается 409
            // booking.add.lockTimeout, а не 500 (R-18).
            var lockResult = await _repository.AcquireBarberLockAsync(
                request.BarberProfileId,
                ct);
            if (lockResult.IsFailure)
            {
                _logger.LogWarning(
                    """
                    Booking lock for barber profile {BarberProfileId} was not acquired
                    within lock_timeout; booking create returns conflict.
                    """,
                    request.BarberProfileId);

                return lockResult.Error;
            }

            var service = await _profiles.GetServiceAsync(
                request.BarberProfileId,
                request.BarberServiceId,
                ct);
            if (service.IsFailure)
            {
                return service.Error;
            }

            if (!service.Value.IsActive)
            {
                return new Failure(Error.NotFound(
                    "booking.create.serviceInactive",
                    $"""
                     Barber service '{request.BarberServiceId}' is inactive and cannot be booked.
                     """,
                    request.BarberServiceId));
            }

            var salonExists = await _salons.ExistsAsync(request.SalonId, ct);
            if (salonExists.IsFailure)
            {
                return salonExists.Error;
            }

            if (!salonExists.Value)
            {
                return new Failure(Error.NotFound(
                    "booking.create.salonNotFound",
                    $"Salon '{request.SalonId}' was not found.",
                    request.SalonId));
            }

            var worksAtSalon = await _profiles.HasSalonAsync(
                request.BarberProfileId,
                request.SalonId,
                ct);
            if (worksAtSalon.IsFailure)
            {
                return worksAtSalon.Error;
            }

            if (!worksAtSalon.Value)
            {
                return new Failure(Error.NotFound(
                    "booking.create.salonNotAssigned",
                    $"""
                     Barber profile '{request.BarberProfileId}' does not work in
                     salon '{request.SalonId}'.
                     """,
                    request.SalonId));
            }

            var booking = Booking.Create(
                userId,
                request.BarberProfileId,
                request.BarberServiceId,
                request.SalonId,
                request.StartAt,
                request.EndAt,
                service.Value.Price,
                idempotencyKey,
                request.CustomerNote);
            if (booking.IsFailure)
            {
                return booking.Error;
            }

            var added = await _repository.AddAsync(booking.Value, ct);
            if (added.IsFailure)
            {
                addFailure = added.Error;

                return added.Error;
            }

            return booking.Value.Id;
        }, cancellationToken);

        if (created.IsSuccess)
        {
            _logger.LogInformation("Booking {BookingId} has been created.", created.Value);

            return created.Value;
        }

        // Транзакция уже откатилась; проверяем, не выиграл ли параллельный запрос
        // с тем же ключом идемпотентности.
        if (addFailure is not null
            && addFailure.Errors.Any(error => error.Type == ErrorType.CONFLICT))
        {
            var raced = await _repository.GetByIdempotencyKeyAsync(
                userId,
                idempotencyKey,
                cancellationToken);
            if (raced.IsSuccess)
            {
                _logger.LogInformation(
                    "Booking {BookingId} has been returned after a conflicting insert.",
                    raced.Value.Id);

                return raced.Value.Id;
            }

            if (addFailure.Errors.Any(error => error.Code.EndsWith(
                    ".uniqueViolation",
                    StringComparison.Ordinal)))
            {
                return new Failure(Error.Conflict(
                    "booking.create.idempotencyKeyConflict",
                    $"""
                     A booking with idempotency key '{idempotencyKey}' already exists
                     for this user.
                     """));
            }
        }

        return created.Error;
    }
}
