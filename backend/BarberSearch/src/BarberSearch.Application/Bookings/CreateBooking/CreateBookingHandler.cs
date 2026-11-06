using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using BarberSearch.Application.Salons;
using BarberSearch.Application.Users;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Users;
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
/// <c>ex_bookings_no_overlap</c> отдаёт <c>23P01</c>, unique-индексы
/// <c>ux_bookings_user_id_idempotency_key</c> (клиент) и
/// <c>ux_bookings_barber_profile_id_idempotency_key_guest</c> (гость) — <c>23505</c>.
/// Оба маппятся в <c>Conflict</c>. После такого отказа обработчик ещё раз читает бронь
/// по ключу в том же скоупе: если параллельный запрос с тем же ключом уже сохранил её,
/// возвращается его идентификатор (идемпотентный ответ), иначе наружу уходит исходный <c>409</c>.
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
/// Барбер может записать клиента сам: <see cref="CreateBookingCommand.CustomerId"/>
/// задаёт зарегистрированного клиента, а <see cref="CreateBookingCommand.ConfirmImmediately"/>
/// сразу переводит бронь в <c>Confirmed</c>. Такой вызов разрешён только владельцу профиля
/// из <see cref="CreateBookingCommand.Request"/> — профиль обязан совпадать с claim'ом
/// <c>barber_profile_id</c>; клиент должен существовать, быть активным и иметь роль
/// <c>Customer</c>, иначе <c>404 booking.create.customerNotFound</c>. Клиентский путь
/// (<c>CustomerId = null</c>) по-прежнему берёт пользователя из токена и создаёт
/// <c>Pending</c>, поэтому передать чужого клиента через публичный контракт нельзя.
/// </para>
/// <para>
/// Вместо аккаунта барбер может записать гостя (walk-in): <see cref="CreateBookingCommand"/>
/// несёт имя и телефон, <c>Booking.UserId</c> остаётся <c>null</c>, а аккаунт не создаётся.
/// Гостевой путь доступен только ручному пути барбера (<c>ConfirmImmediately = true</c>);
/// публичный путь клиента гостевые поля отклоняет как
/// <c>400 booking.create.customerAmbiguous</c>, потому что клиент там уже задан токеном.
/// Заполненные одновременно <c>CustomerId</c> и гостевые поля — тот же код. Ручной путь без
/// клиента и без гостя — <c>400 booking.create.customer</c>; пустое имя или телефон гостя
/// проверяет домен (<c>booking.create.guestFirstName</c> / <c>booking.create.guestPhone</c>).
/// </para>
/// <para>
/// Ключ идемпотентности клиента остаётся в скоупе <c>(user_id, key)</c>, гостя — в скоупе
/// <c>(barber_profile_id, key)</c>, потому что <c>user_id</c> у гостя нет. Поэтому гостевые
/// брони разных барберов с одинаковым ключом не конфликтуют, а гостевая и клиентская бронь
/// с одинаковым ключом — тоже.
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
    /// <summary>
    /// Публичный код ошибки «не передан ни зарегистрированный клиент, ни гость»; <c>400</c>.
    /// </summary>
    private const string CUSTOMER_REQUIRED_CODE = "booking.create.customer";

    /// <summary>
    /// Публичный код ошибки «клиент и гость одновременно»; <c>400</c>.
    /// </summary>
    private const string CUSTOMER_AMBIGUOUS_CODE = "booking.create.customerAmbiguous";

    private readonly IBookingRepository _repository;
    private readonly IBarberProfileRepository _profiles;
    private readonly ISalonRepository _salons;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CreateBookingHandler> _logger;

    public CreateBookingHandler(
        IBookingRepository repository,
        IBarberProfileRepository profiles,
        ISalonRepository salons,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<CreateBookingHandler> logger)
    {
        _repository = repository;
        _profiles = profiles;
        _salons = salons;
        _users = users;
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

        var actorId = userIdResult.Value;

        // Наличие гостевых полей считается по исходным значениям: даже строка из пробелов —
        // это попытка записать гостя, и молча проигнорировать её нельзя.
        var hasGuestContact = command.GuestFirstName is not null
            || command.GuestLastName is not null
            || command.GuestPhone is not null;

        // Гость — только ручной путь барбера. Публичный путь клиента берёт клиента из токена,
        // поэтому гостевые поля в нём означают попытку задать двух клиентов сразу.
        if (hasGuestContact && !command.ConfirmImmediately)
        {
            return new Failure(Error.Validation(
                CUSTOMER_AMBIGUOUS_CODE,
                "Guest contact fields can only be used when a barber creates a booking manually.",
                nameof(command.GuestFirstName)));
        }

        // Клиент и гость одновременно недопустимы: бронь принадлежит ровно одному из них.
        if (hasGuestContact && command.CustomerId is not null)
        {
            return new Failure(Error.Validation(
                CUSTOMER_AMBIGUOUS_CODE,
                "A booking cannot belong to a registered customer and a guest at the same time.",
                nameof(command.GuestFirstName)));
        }

        Guid? customerId = null;
        var isGuestBooking = false;

        // Ручной путь барбера (клиент или гость) разрешён только владельцу профиля из claim'а.
        if (hasGuestContact || command.CustomerId is not null)
        {
            if (!_currentUser.TryGetBarberProfileId(out var actorProfileId)
                || actorProfileId != request.BarberProfileId)
            {
                return new Failure(Error.Forbidden(
                    ApiErrorCodes.Auth.Forbidden,
                    "A barber can create a booking only for their own profile."));
            }
        }

        if (hasGuestContact)
        {
            isGuestBooking = true;
        }
        else if (command.CustomerId is { } requestedCustomerId)
        {
            customerId = requestedCustomerId;
        }
        else if (command.ConfirmImmediately)
        {
            // Ручной путь барбера обязан выбрать клиента: зарегистрированного или гостя.
            // Публичный путь сюда не попадает — при ConfirmImmediately = false клиент
            // берётся из токена, и эта ветка недостижима.
            return new Failure(Error.Validation(
                CUSTOMER_REQUIRED_CODE,
                "A booking must be created either for a registered customer or for a guest.",
                nameof(command.CustomerId)));
        }
        else
        {
            customerId = actorId;
        }

        // Ключ сравнивается и сохраняется в одном виде: иначе " key " и "key" были бы
        // разными ключами для unique-индекса, хотя клиент считает их одним запросом.
        // Проверка идёт до чтения из репозитория: иначе пустой ключ превращался бы в ошибку
        // инфраструктуры вместо доменного 400 booking.create.idempotencyKey (R-09).
        var idempotencyKey = Booking.NormalizeIdempotencyKey(request.IdempotencyKey);

        if (Booking.ValidateIdempotencyKey(idempotencyKey) is { } idempotencyKeyError)
        {
            return new Failure(idempotencyKeyError);
        }

        // Скоуп ключа разный: у клиента — (user_id, key), у гостя user_id нет, поэтому
        // ключ гостя живёт в скоупе (barber_profile_id, key).
        var existing = isGuestBooking
            ? await _repository.GetByGuestIdempotencyKeyAsync(
                request.BarberProfileId,
                idempotencyKey,
                cancellationToken)
            : await _repository.GetByIdempotencyKeyAsync(
                customerId!.Value,
                idempotencyKey,
                cancellationToken);

        if (existing.IsSuccess)
        {
            // Ключ уникален в рамках клиента: барбер не должен получить по нему бронь
            // другого мастера, даже если клиент случайно использовал тот же ключ.
            if (!isGuestBooking
                && command.CustomerId is not null
                && existing.Value.BarberProfileId != request.BarberProfileId)
            {
                return new Failure(Error.Conflict(
                    "booking.create.idempotencyKeyConflict",
                    $"""
                     A booking with idempotency key '{idempotencyKey}' already exists
                     for this customer at another barber.
                     """));
            }

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

        // Клиента проверяем после ключа идемпотентности: повтор уже созданной брони
        // обязан вернуть её id, даже если клиента успели заблокировать. У гостя аккаунта
        // нет, поэтому проверять нечего — домен требует только имя и телефон.
        if (!isGuestBooking && command.CustomerId is not null)
        {
            var customer = await _users.GetByIdAsync(customerId!.Value, cancellationToken);

            if (customer.IsFailure
                || customer.Value.Role != UserRole.Customer
                || !customer.Value.IsActive)
            {
                return new Failure(Error.NotFound(
                    ApiErrorCodes.Booking.CreateCustomerNotFound,
                    $"Customer '{customerId}' was not found or cannot be booked.",
                    customerId));
            }
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

            // У гостя UserId = null, а имя и телефон — снимок контакта в самой брони.
            // Домен проверяет инвариант «клиент или гость» и нормализует контакт.
            var booking = Booking.Create(
                customerId,
                request.BarberProfileId,
                request.BarberServiceId,
                request.SalonId,
                request.StartAt,
                request.EndAt,
                service.Value.Price,
                idempotencyKey,
                request.CustomerNote,
                command.GuestFirstName,
                command.GuestLastName,
                command.GuestPhone);
            if (booking.IsFailure)
            {
                return booking.Error;
            }

            // Запись, которую барбер вносит сам, не ждёт подтверждения: он и есть
            // подтверждающая сторона. Клиентский путь остаётся Pending.
            if (command.ConfirmImmediately)
            {
                var confirmed = booking.Value.Confirm();
                if (confirmed.IsFailure)
                {
                    return confirmed.Error;
                }
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
        // с тем же ключом идемпотентности — в том же скоупе, в котором его искали.
        if (addFailure is not null
            && addFailure.Errors.Any(error => error.Type == ErrorType.CONFLICT))
        {
            var raced = isGuestBooking
                ? await _repository.GetByGuestIdempotencyKeyAsync(
                    request.BarberProfileId,
                    idempotencyKey,
                    cancellationToken)
                : await _repository.GetByIdempotencyKeyAsync(
                    customerId!.Value,
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
                     for this customer.
                     """));
            }
        }

        return created.Error;
    }
}
