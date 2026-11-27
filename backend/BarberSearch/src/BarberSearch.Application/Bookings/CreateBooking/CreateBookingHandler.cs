using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.BarberProfiles.GetBarberSlots;
using BarberSearch.Application.Common;
using BarberSearch.Application.Push.BarberBooking;
using BarberSearch.Application.Push.BookingCreated;
using BarberSearch.Application.Salons;
using BarberSearch.Application.Users;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Common;
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
/// задаёт зарегистрированного пользователя, а <see cref="CreateBookingCommand.ConfirmImmediately"/>
/// сразу переводит бронь в <c>Confirmed</c>. Такой вызов разрешён только владельцу профиля
/// из <see cref="CreateBookingCommand.Request"/> — профиль обязан совпадать с claim'ом
/// <c>barber_profile_id</c>; пользователь должен существовать и быть активным, роль при этом
/// не важна (клиент, администратор, барбер и т. д.), иначе
/// <c>404 booking.create.customerNotFound</c>. Клиентский путь
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
/// <para>
/// Внутри той же транзакции, сразу после вставки брони, ставится push-событие о её создании
/// (<see cref="IBookingCreatedPushNotifier"/>): строка очереди и бронь коммитятся вместе, а
/// воркер доставит событие на следующем тике. Гостю событие не создаётся (у брони нет
/// получателя), а сбой постановки в очередь не роняет создание — бронь важнее уведомления,
/// поэтому ошибка только логируется предупреждением с идентификатором брони.
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
    private readonly IBookingCreatedPushNotifier _pushNotifier;
    private readonly IBarberBookingPushNotifier _barberPushNotifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CreateBookingHandler> _logger;

    public CreateBookingHandler(
        IBookingRepository repository,
        IBarberProfileRepository profiles,
        ISalonRepository salons,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IBookingCreatedPushNotifier pushNotifier,
        IBarberBookingPushNotifier barberPushNotifier,
        TimeProvider timeProvider,
        ILogger<CreateBookingHandler> logger)
    {
        _repository = repository;
        _profiles = profiles;
        _salons = salons;
        _users = users;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _pushNotifier = pushNotifier;
        _barberPushNotifier = barberPushNotifier;
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

        // Пользователя проверяем после ключа идемпотентности: повтор уже созданной брони
        // обязан вернуть её id, даже если пользователя успели заблокировать. Роль не важна:
        // записать можно клиента, администратора, барбера и любого другого активного
        // пользователя. У гостя аккаунта нет, поэтому проверять нечего — домен требует
        // только имя и телефон.
        if (!isGuestBooking && command.CustomerId is not null)
        {
            var customer = await _users.GetByIdAsync(customerId!.Value, cancellationToken);

            if (customer.IsFailure || !customer.Value.IsActive)
            {
                return new Failure(Error.NotFound(
                    ApiErrorCodes.Booking.CreateCustomerNotFound,
                    $"User '{customerId}' was not found or cannot be booked.",
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

            var salon = await _salons.GetByIdAsync(request.SalonId, ct);
            if (salon.IsFailure)
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

            // Длительность и конец визита задаёт услуга, а не запрос: клиент выбирает
            // только начало. Поэтому время проверяется тем же расчётом свободных окон,
            // что отдаёт GET api/barbers/{id}/slots, — иначе в дне мастера появилась бы
            // щель, которую уже никто не займёт.
            if (!SalonTimeZone.TryResolve(salon.Value.TimeZoneId, out var timeZone))
            {
                _logger.LogError(
                    "Salon {SalonId} of barber profile {BarberProfileId} has an unresolvable time zone '{TimeZoneId}'; booking create is rejected.",
                    salon.Value.Id,
                    request.BarberProfileId,
                    salon.Value.TimeZoneId);

                return new Failure(Error.Conflict(
                    "barberSlots.timeZone.unknown",
                    $"Salon time zone '{salon.Value.TimeZoneId}' cannot be resolved."));
            }

            var localStart = TimeZoneInfo.ConvertTime(request.StartAt, timeZone);
            var localDate = DateOnly.FromDateTime(localStart.DateTime);

            // Публичная запись видит только опубликованный профиль — тот же фильтр, что
            // у публичных слотов; кабинет барбера работает и до публикации, поэтому ему
            // собственный контекст. Иначе клиент с известными GUID'ами записался бы
            // к непроверенному мастеру, которому слоты не отдаются.
            var contextResult = command.ConfirmImmediately
                ? await _profiles.GetOwnSlotContextAsync(
                    request.BarberProfileId,
                    request.SalonId,
                    service.Value.HairServiceId,
                    localDate,
                    ct)
                : await _profiles.GetSlotContextAsync(
                    request.BarberProfileId,
                    request.SalonId,
                    service.Value.HairServiceId,
                    localDate,
                    ct);
            if (contextResult.IsFailure)
            {
                return contextResult.Error;
            }

            var context = contextResult.Value;

            // Защита от гонки «услугу скрыли между проверкой IsActive и чтением контекста»:
            // контекст читает только активную строку, поэтому пара обязана совпасть.
            // Дату не сверяем: её задаёт сам вызов, а репозиторий строит контекст на неё.
            if (context.SalonId != request.SalonId)
            {
                return new Failure(Error.NotFound(
                    "booking.create.salonNotFound",
                    $"Salon '{request.SalonId}' is not available for booking on {localDate:yyyy-MM-dd}.",
                    request.SalonId));
            }

            if (context.BarberServiceId != request.BarberServiceId)
            {
                return new Failure(Error.NotFound(
                    "booking.create.serviceInactive",
                    $"""
                     Barber service '{request.BarberServiceId}' is inactive and cannot be booked.
                     """,
                    request.BarberServiceId));
            }

            if (!context.IsWorkingDay)
            {
                return new Failure(Error.Conflict(
                    ApiErrorCodes.Booking.CreateStartAtOutsideSchedule,
                    $"The barber does not work on {localDate:yyyy-MM-dd}."));
            }

            var dayStart = SalonTimeZone.ToUtc(localDate.ToDateTime(TimeOnly.MinValue), timeZone);
            var dayEnd = SalonTimeZone.ToUtc(localDate.AddDays(1).ToDateTime(TimeOnly.MinValue), timeZone);

            if (dayStart is null || dayEnd is null)
            {
                _logger.LogError(
                    "Local day boundaries for '{Date}' cannot be converted to UTC in time zone '{TimeZoneId}'.",
                    localDate,
                    context.TimeZoneId);

                return new Failure(Error.Conflict(
                    ApiErrorCodes.Booking.CreateStartAtOutsideSchedule,
                    $"Local day boundaries for '{localDate}' cannot be converted to UTC."));
            }

            var active = await _repository.GetActiveByBarberAsync(
                request.BarberProfileId,
                dayStart.Value,
                dayEnd.Value,
                ct);
            if (active.IsFailure)
            {
                return active.Error;
            }

            var busy = active.Value
                .Select(booking => new BusyInterval(booking.StartAt, booking.EndAt))
                .ToList();

            BarberSlotResponse? slot;

            if (command.ConfirmImmediately)
            {
                // Ручной путь барбера: время не обязано совпадать с шагом сетки (клиент
                // пришёл к 10:07), но визит целиком помещается в рабочий день и не
                // пересекается ни с отпуском, ни с чужой бронью.
                var fit = SlotGrid.Fit(
                    localDate,
                    context.TimeZoneId,
                    context.StartTime,
                    context.EndTime,
                    context.DurationMinutes,
                    context.TimeOffs,
                    busy,
                    request.StartAt,
                    out slot);

                if (fit == SlotFit.OutsideSchedule)
                {
                    return new Failure(Error.Conflict(
                        ApiErrorCodes.Booking.CreateStartAtOutsideSchedule,
                        "The requested time is outside the barber's working hours or time off."));
                }

                if (fit == SlotFit.Overlap)
                {
                    return new Failure(Error.Conflict(
                        ApiErrorCodes.Booking.CreateStartAtOverlap,
                        "The requested time is already taken."));
                }
            }
            else
            {
                // Публичный путь: начало обязано быть стартом из сетки. Клиент мог
                // сохранить список слотов до чужой записи, поэтому запрос проверяется
                // заново, а не доверяется присланному времени.
                slot = SlotGrid
                    .Legal(
                        localDate,
                        context.TimeZoneId,
                        context.StartTime,
                        context.EndTime,
                        context.DurationMinutes,
                        context.TimeOffs,
                        busy)
                    .FirstOrDefault(candidate => candidate.StartAt == request.StartAt);

                if (slot is null)
                {
                    // Причина отказа выбирается по тому, что именно мешает: занятое
                    // время отвечает booking.add.overlap, как и exclusion-ограничение.
                    var fit = SlotGrid.Fit(
                        localDate,
                        context.TimeZoneId,
                        context.StartTime,
                        context.EndTime,
                        context.DurationMinutes,
                        context.TimeOffs,
                        busy,
                        request.StartAt,
                        out _);

                    return fit switch
                    {
                        SlotFit.Overlap => new Failure(Error.Conflict(
                            ApiErrorCodes.Booking.AddOverlap,
                            "The requested time is already taken.")),
                        SlotFit.OutsideSchedule => new Failure(Error.Conflict(
                            ApiErrorCodes.Booking.CreateStartAtOutsideSchedule,
                            "The requested time is outside the barber's working hours or time off.")),
                        _ => new Failure(Error.Conflict(
                            ApiErrorCodes.Booking.CreateStartAtNotASlot,
                            "The requested time is not an available slot for this service."))
                    };
                }
            }

            var chosenSlot = slot!;

            // У гостя UserId = null, а имя и телефон — снимок контакта в самой брони.
            // Домен проверяет инвариант «клиент или гость» и нормализует контакт.
            var booking = Booking.Create(
                customerId,
                request.BarberProfileId,
                request.BarberServiceId,
                request.SalonId,
                chosenSlot.StartAt,
                chosenSlot.EndAt,
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

            // Событие создания ставится в очередь в этой же транзакции: бронь и строки push
            // коммитятся вместе. Клиентское событие и событие мастеру — разные получатели
            // и разные тексты, поэтому это две строки. Сбой уведомления не должен откатывать
            // бронь — см. NotifyBookingCreatedAsync и BarberBookingPush.
            var createdByBarber = command.CustomerId is not null || hasGuestContact;

            await NotifyBookingCreatedAsync(booking.Value, createdByBarber, ct);

            await BarberBookingPush.NotifyCreatedAsync(
                _barberPushNotifier,
                _logger,
                booking.Value,
                createdByBarber,
                ct);

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

    /// <summary>
    /// Ставит push-событие о создании брони, не роняя саму бронь.
    /// </summary>
    /// <remarks>
    /// Ошибка постановки в очередь — не ошибка записи: клиент уже получил подтверждение, и
    /// откатывать бронь из-за push нельзя. В журнал попадают только идентификатор брони и
    /// коды/тип ошибки: ни имени, ни телефона, ни заметки клиента, ни текста сообщения (152-ФЗ).
    /// Отмена запроса пробрасывается — незавершённая транзакция всё равно откатится
    /// <see cref="IUnitOfWork"/>, а исключение транспорта/базы не выходит наружу.
    /// </remarks>
    private async Task NotifyBookingCreatedAsync(
        Booking booking,
        bool createdByBarber,
        CancellationToken cancellationToken)
    {
        try
        {
            var notified = await _pushNotifier.NotifyAsync(
                booking,
                createdByBarber,
                cancellationToken);
            if (notified.IsFailure)
            {
                _logger.LogWarning(
                    "Booking {BookingId} was created, but its push notification was not enqueued: {Codes}.",
                    booking.Id,
                    string.Join(", ", notified.Error.Errors.Select(error => error.Code)));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Сбой push не должен откатывать уже вставленную бронь.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // Тип, а не текст исключения: сообщение может содержать параметры SQL-запроса.
            _logger.LogWarning(
                "Booking {BookingId} was created, but its push notification failed: {ErrorType}.",
                booking.Id,
                exception.GetType().Name);
        }
    }
}
