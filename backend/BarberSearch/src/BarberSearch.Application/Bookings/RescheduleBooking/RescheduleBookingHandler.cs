using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Bookings.BookingChangeEmail;
using BarberSearch.Application.Common;
using BarberSearch.Application.Push.BookingStatus;
using BarberSearch.Application.Salons;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain;
using BarberSearch.Domain.Bookings;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Bookings.RescheduleBooking;

/// <summary>
/// Обработчик команды <see cref="RescheduleBookingCommand"/>: перенос визита на другое время.
/// </summary>
/// <remarks>
/// <para>
/// Меняются только <c>StartAt</c> и <c>EndAt</c> (D2): услуга, салон, барбер, цена и статус
/// остаются прежними. Длительность берётся из самой брони, а не из текущего прайса (D6), и
/// новое время обязано быть стартом слота сетки (D5) — иначе календарь получил бы «полуслоты».
/// </para>
/// <para>
/// Атомарность (D8): транзакцией владеет <see cref="IUnitOfWork"/>; advisory-блокировка барбера
/// берётся до повторного чтения брони, иначе EF вернул бы ту же отслеживаемую сущность и
/// проверки увидели бы устаревшие данные. Профиль барбера для ключа блокировки читается
/// узкой проекцией <see cref="IBookingRepository.GetBarberProfileIdAsync"/> ещё до транзакции.
/// </para>
/// <para>
/// Права те же, что у отмены (D3): клиент-владелец, барбер профиля или администратор.
/// Гостевую запись двигает только барбер или администратор. Занятость проверяется по активным
/// броням дня с исключением самой брони (D9): своя запись слот не занимает. Остаточная гонка
/// на exclusion-ограничении вернёт <c>booking.update.overlap</c> — клиент покажет её как
/// «время занято».
/// </para>
/// <para>
/// Пояс салона читается отдельно <b>до</b> контекста дня: без него нельзя понять, какая
/// локальная дата у нового времени (у полуночи UTC-дата и дата салона различаются). Повторное
/// чтение салона внутри контекста — осознанная плата за отсутствие нового метода в чужом
/// репозитории, а не повод кэшировать строку.
/// </para>
/// <para>
/// После успешного коммита уведомитель вызывается <b>всегда</b>, когда время изменилось:
/// вид выбирается по <c>isClientOwner</c>, а молчание — по поверхности. Перенос самим
/// клиентом в приложении push не создаёт, но ставит письмо на подтверждённый адрес; перенос
/// с веба и перенос барбером или администратором уведомляют push'ем. Перенос на то же время
/// вообще не доходит до записи и не уведомляет (<c>D15</c>). Событие ставится <b>вне</b>
/// транзакции переноса: ошибка вставки в push-очередь не должна отравлять транзакцию, а сбой
/// постановки логируется предупреждением и не меняет результат команды.
/// </para>
/// </remarks>
public sealed class RescheduleBookingHandler : ICommandHandler<RescheduleBookingCommand>
{
    private readonly IBookingRepository _repository;
    private readonly IBarberProfileRepository _profiles;
    private readonly ISalonRepository _salons;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IBookingStatusPushNotifier _pushNotifier;
    private readonly IBookingChangeEmailNotifier _emailNotifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RescheduleBookingHandler> _logger;

    public RescheduleBookingHandler(
        IBookingRepository repository,
        IBarberProfileRepository profiles,
        ISalonRepository salons,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IBookingStatusPushNotifier pushNotifier,
        IBookingChangeEmailNotifier emailNotifier,
        TimeProvider timeProvider,
        ILogger<RescheduleBookingHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(salons);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(pushNotifier);
        ArgumentNullException.ThrowIfNull(emailNotifier);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _profiles = profiles;
        _salons = salons;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _pushNotifier = pushNotifier;
        _emailNotifier = emailNotifier;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Переносит визит на новое время или возвращает причину отказа.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        RescheduleBookingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userIdResult = _currentUser.RequireUserId();
        if (userIdResult.IsFailure)
        {
            return userIdResult.Error;
        }

        // Ключ advisory-блокировки — профиль барбера, поэтому он читается до транзакции.
        // Бронь здесь не читается: её сущность обязана быть свежей уже после блокировки.
        var targetResult = await _repository.GetBarberProfileIdAsync(
            command.BookingId,
            cancellationToken);
        if (targetResult.IsFailure)
        {
            return targetResult.Error;
        }

        var barberProfileId = targetResult.Value;
        var currentUserId = userIdResult.Value;

        var rescheduled = await _unitOfWork.ExecuteAsync(
            token => RescheduleAsync(command, currentUserId, barberProfileId, token),
            cancellationToken);
        if (rescheduled.IsFailure)
        {
            return rescheduled.Error;
        }

        // Push и письмо ставятся после коммита, а не внутри транзакции: ошибка вставки в очередь
        // в PostgreSQL делает транзакцию недействительной, и «проглоченный» сбой push
        // откатил бы уже сохранённый перенос на коммите. Здесь же сбой уведомления
        // остаётся сбоем уведомления — бронь важнее.
        var outcome = rescheduled.Value;

        // D15: перенос на то же время — успех без записи и без уведомлений.
        if (outcome.TimeChanged)
        {
            var change = outcome.IsClientOwner
                ? BookingStatusChange.RescheduledByCustomer
                : BookingStatusChange.RescheduledByBarber;

            await BookingStatusPush.NotifyAsync(
                _pushNotifier,
                _logger,
                outcome.Booking,
                change,
                cancellationToken);

            // Письмо уходит только за перенос из приложения; решение — в уведомителе.
            await _emailNotifier.NotifyRescheduledAsync(
                outcome.Booking,
                outcome.IsClientOwner,
                cancellationToken);
        }

        return UnitResult.Success<Failure>();
    }

    private async Task<Result<RescheduleOutcome, Failure>> RescheduleAsync(
        RescheduleBookingCommand command,
        Guid currentUserId,
        Guid barberProfileId,
        CancellationToken cancellationToken)
    {
        var lockResult = await _repository.AcquireBarberLockAsync(
            barberProfileId,
            ApiErrorCodes.Booking.RescheduleLockTimeout,
            cancellationToken);
        if (lockResult.IsFailure)
        {
            _logger.LogWarning(
                """
                Reschedule lock for barber profile {BarberProfileId} was not acquired
                within lock_timeout; reschedule returns conflict.
                """,
                barberProfileId);

            return lockResult.Error;
        }

        // Только после блокировки: прочитанная раньше сущность осталась бы в change tracker
        // с устаревшими временем и статусом (D8).
        var bookingResult = await _repository.GetByIdAsync(command.BookingId, cancellationToken);
        if (bookingResult.IsFailure)
        {
            return bookingResult.Error;
        }

        var booking = bookingResult.Value;

        // У гостевой брони клиента-владельца нет: её двигает барбер профиля или администратор.
        var isClientOwner = booking.UserId is { } owner && owner == currentUserId;

        if (!isClientOwner)
        {
            var allowed = await _currentUser.EnsureIsBarberOfAsync(
                _profiles,
                booking.BarberProfileId,
                cancellationToken);
            if (allowed.IsFailure)
            {
                return allowed.Error;
            }
        }

        var now = _timeProvider.GetUtcNow();

        // Начавшийся визит переносить поздно: для него есть завершение и неявка. Отдельный
        // код от «новое время в прошлом» — иначе вчерашнюю бронь можно было бы «оживить».
        if (booking.StartAt <= now)
        {
            return new Failure(Error.Conflict(
                ApiErrorCodes.Booking.RescheduleStatus,
                "The booking has already started."));
        }

        if (command.StartAt <= now)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Booking.RescheduleStartAtInPast,
                "A booking cannot be rescheduled to a time in the past.",
                nameof(command.StartAt)));
        }

        if (command.StartAt > now.AddDays(Constants.BOOKING_HORIZON_DAYS))
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Booking.RescheduleStartAtTooFar,
                $"A booking cannot be rescheduled more than {Constants.BOOKING_HORIZON_DAYS} days ahead.",
                nameof(command.StartAt)));
        }

        // D15: перенос на то же время — успех без записи. Статус проверяет домен: терминальная
        // бронь не становится «перенесённой» только потому, что время не изменилось.
        if (command.StartAt == booking.StartAt)
        {
            var unchanged = booking.Reschedule(command.StartAt, booking.EndAt);
            if (unchanged.IsFailure)
            {
                return unchanged.Error;
            }

            _logger.LogInformation(
                "Booking {BookingId} reschedule to the same start time was a no-op.",
                booking.Id);

            return new RescheduleOutcome(booking, isClientOwner, TimeChanged: false);
        }

        // Пояс салона нужен до контекста дня: только он даёт локальную дату нового времени.
        var salonResult = await _salons.GetByIdAsync(booking.SalonId, cancellationToken);
        if (salonResult.IsFailure)
        {
            _logger.LogError(
                "Salon {SalonId} of booking {BookingId} was not found; the booking cannot be rescheduled.",
                booking.SalonId,
                booking.Id);

            return new Failure(Error.Conflict(
                ApiErrorCodes.Booking.RescheduleSlot,
                "The salon of the booking was not found."));
        }

        if (!SalonTimeZone.TryResolve(salonResult.Value.TimeZoneId, out var timeZone))
        {
            _logger.LogError(
                "Salon {SalonId} of booking {BookingId} has an unresolvable time zone '{TimeZoneId}'; the booking cannot be rescheduled.",
                booking.SalonId,
                booking.Id,
                salonResult.Value.TimeZoneId);

            return new Failure(Error.Conflict(
                ApiErrorCodes.Booking.RescheduleSlot,
                $"Salon time zone '{salonResult.Value.TimeZoneId}' cannot be resolved."));
        }

        var localDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(command.StartAt, timeZone).DateTime);

        var durationMinutes = BookingDuration.MinutesOf(booking);

        if (durationMinutes <= 0)
        {
            _logger.LogError(
                "Booking {BookingId} has a non-positive duration ({DurationMinutes} minutes); it cannot be rescheduled.",
                booking.Id,
                durationMinutes);

            return new Failure(Error.Conflict(
                ApiErrorCodes.Booking.RescheduleSlot,
                "The booking duration is invalid."));
        }

        // Видимость разная: клиент-владелец видит только публичный профиль, барбер и админ —
        // свой, даже если профиль ещё не опубликован. Длительность задаёт бронь, поэтому
        // услуга справочника в контекст не передаётся.
        var contextResult = isClientOwner
            ? await _profiles.GetSlotContextAsync(
                booking.BarberProfileId,
                booking.SalonId,
                hairServiceId: null,
                localDate,
                cancellationToken)
            : await _profiles.GetOwnSlotContextAsync(
                booking.BarberProfileId,
                booking.SalonId,
                hairServiceId: null,
                localDate,
                cancellationToken);
        if (contextResult.IsFailure)
        {
            return contextResult.Error;
        }

        var context = contextResult.Value;

        if (!context.IsWorkingDay)
        {
            return new Failure(Error.Conflict(
                ApiErrorCodes.Booking.RescheduleSlot,
                $"The barber does not work on {localDate:yyyy-MM-dd}."));
        }

        // Сравнение по абсолютному моменту: строка локали не должна решать, тот ли это слот.
        var slot = SlotGrid
            .Legal(
                localDate,
                context.TimeZoneId,
                context.StartTime,
                context.EndTime,
                durationMinutes,
                context.TimeOffs)
            .FirstOrDefault(candidate => candidate.StartAt == command.StartAt);

        if (slot is null)
        {
            return new Failure(Error.Conflict(
                ApiErrorCodes.Booking.RescheduleSlot,
                "The requested time is not a legal slot start."));
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
                ApiErrorCodes.Booking.RescheduleSlot,
                $"Local day boundaries for '{localDate}' cannot be converted to UTC."));
        }

        var activeResult = await _repository.GetActiveByBarberAsync(
            booking.BarberProfileId,
            dayStart.Value,
            dayEnd.Value,
            cancellationToken);
        if (activeResult.IsFailure)
        {
            return activeResult.Error;
        }

        // Своя бронь слот не занимает: иначе перенос на соседний слот упирался бы в себя же.
        if (activeResult.Value.Any(other =>
                other.Id != booking.Id
                && other.StartAt < slot.EndAt
                && slot.StartAt < other.EndAt))
        {
            return new Failure(Error.Conflict(
                ApiErrorCodes.Booking.RescheduleOverlap,
                "The requested time is already taken."));
        }

        // Конец берётся у слота, а не пересчитывается как start + duration: на дне перевода
        // часов ячейка может быть длиннее/короче в абсолютных минутах (D6).
        var rescheduled = booking.Reschedule(slot.StartAt, slot.EndAt);
        if (rescheduled.IsFailure)
        {
            return rescheduled.Error;
        }

        // Остаточная гонка: exclusion-ограничение вернёт booking.update.overlap, а параллельное
        // подтверждение — booking.update.concurrencyConflict. Оба кода пробрасываются как есть.
        var saved = await _repository.UpdateAsync(booking, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Booking {BookingId} has been rescheduled to {StartAt}.",
            booking.Id,
            booking.StartAt);

        // Снимок берётся из сохранённой брони, поэтому start_at строки — новое время визита.
        // Автор и поверхность решают, что именно уйдёт: push, письмо или ничего. Гейт
        // поверхности стоит в уведомителях, а не здесь (AGENT.md §20.11).
        return new RescheduleOutcome(booking, isClientOwner, TimeChanged: true);
    }

    /// <summary>
    /// Итог успешного переноса: сохранённая бронь, автор и признак «время изменилось».
    /// </summary>
    /// <param name="Booking">Бронь с новым временем; для no-op — с прежним.</param>
    /// <param name="IsClientOwner">Перенос сделал клиент-владелец, а не сотрудник салона.</param>
    /// <param name="TimeChanged">
    /// <c>false</c> для no-op <c>D15</c>: времени не изменилось, уведомлять не о чем.
    /// </param>
    private sealed record RescheduleOutcome(Booking Booking, bool IsClientOwner, bool TimeChanged);
}
