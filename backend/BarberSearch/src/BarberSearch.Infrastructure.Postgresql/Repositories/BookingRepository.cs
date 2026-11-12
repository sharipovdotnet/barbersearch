using BarberSearch.Application.Bookings;
using BarberSearch.Application.Bookings.Admin;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Bookings.Admin;
using BarberSearch.Contracts.Bookings.Barber;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Common;
using BarberSearch.Infrastructure.Postgresql.Audit;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий агрегата <see cref="Booking"/>.
/// </summary>
public sealed class BookingRepository : RepositoryBase<Booking>, IBookingRepository
{
    public BookingRepository(ApplicationDbContext context, AuditTrail? auditTrail = null)
        : base(context, auditTrail)
    {
    }

    public async Task<Result<Booking, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var booking = await Context.Bookings
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (booking is null)
        {
            return NotFound(ApiErrorCodes.Booking.NotFound, nameof(Booking), id);
        }

        return booking;
    }

    public async Task<Result<Booking, Failure>> GetByIdempotencyKeyAsync(
        Guid userId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Booking.IdempotencyKeyInvalid,
                DomainMessages.IDEMPOTENCY_KEY_REQUIRED,
                nameof(idempotencyKey)));
        }

        var key = idempotencyKey.Trim();

        // Обработчику нужен только идентификатор существующей брони, поэтому чтение
        // идёт без отслеживания (AGENT.md §15.6).
        var booking = await Context.Bookings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.IdempotencyKey == key,
                cancellationToken);

        if (booking is null)
        {
            return new Failure(Error.NotFound(
                ApiErrorCodes.Booking.IdempotencyKeyNotFound,
                $"Booking with idempotency key '{key}' was not found for user '{userId}'."));
        }

        return booking;
    }

    public async Task<Result<Booking, Failure>> GetByGuestIdempotencyKeyAsync(
        Guid barberProfileId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Booking.IdempotencyKeyInvalid,
                DomainMessages.IDEMPOTENCY_KEY_REQUIRED,
                nameof(idempotencyKey)));
        }

        var key = idempotencyKey.Trim();

        // Гостевой ключ скоупится парой (барбер, ключ): user_id у гостя нет, а уникальный
        // индекс клиента NULL-строки не сравнивает. Условие повторяет фильтр частичного
        // индекса ux_bookings_barber_profile_id_idempotency_key_guest.
        var booking = await Context.Bookings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.UserId == null
                     && x.BarberProfileId == barberProfileId
                     && x.IdempotencyKey == key,
                cancellationToken);

        if (booking is null)
        {
            return new Failure(Error.NotFound(
                ApiErrorCodes.Booking.IdempotencyKeyNotFound,
                $"""
                 Guest booking with idempotency key '{key}' was not found for
                 barber profile '{barberProfileId}'.
                 """));
        }

        return booking;
    }

    public async Task<Result<IReadOnlyList<Booking>, Failure>> GetByUserAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (ValidatePaging(
                page,
                pageSize,
                ApiErrorCodes.Booking.PagingPageInvalid,
                ApiErrorCodes.Booking.PagingPageSizeInvalid) is { } pagingError)
        {
            return pagingError;
        }

        var bookings = await Context.Bookings
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.StartAt)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return bookings;
    }

    public async Task<Result<IReadOnlyList<BookingResponse>, Failure>> GetPageAsync(
        Guid? userId,
        BookingScope scope,
        DateTimeOffset now,
        BookingCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Booking.PageLimitInvalid,
                Messages.PAGE_LIMIT_NOT_POSITIVE,
                nameof(limit)));
        }

        var query = Context.Bookings.AsNoTracking();

        if (userId is { } filterUserId)
        {
            query = query.Where(booking => booking.UserId == filterUserId);
        }

        // Граница скоупа — EndAt, а не статус: активная бронь с прошедшим концом уже
        // история, а терминальный статус никогда не попадает в «ближайшие».
        query = scope switch
        {
            BookingScope.Upcoming => query.Where(booking =>
                (booking.Status == BookingStatus.Pending
                 || booking.Status == BookingStatus.Confirmed)
                && booking.EndAt > now),
            BookingScope.Past => query.Where(booking =>
                (booking.Status != BookingStatus.Pending
                 && booking.Status != BookingStatus.Confirmed)
                || booking.EndAt <= now),
            _ => query
        };

        // Направление keyset-сравнения задаёт скоуп: «ближайшие» идут вперёд,
        // история — назад, а `all` сохраняет прежний тай-брейк `id ASC` при
        // `start_at DESC`. Поэтому три ветки, а не две.
        if (cursor is { } position)
        {
            query = scope switch
            {
                BookingScope.Upcoming => query.Where(booking =>
                    booking.StartAt > position.StartAt
                    || (booking.StartAt == position.StartAt
                        && booking.Id.CompareTo(position.Id) > 0)),
                BookingScope.Past => query.Where(booking =>
                    booking.StartAt < position.StartAt
                    || (booking.StartAt == position.StartAt
                        && booking.Id.CompareTo(position.Id) < 0)),
                _ => query.Where(booking =>
                    booking.StartAt < position.StartAt
                    || (booking.StartAt == position.StartAt
                        && booking.Id.CompareTo(position.Id) > 0))
            };
        }

        var ordered = scope switch
        {
            BookingScope.Upcoming => query
                .OrderBy(booking => booking.StartAt)
                .ThenBy(booking => booking.Id),
            BookingScope.Past => query
                .OrderByDescending(booking => booking.StartAt)
                .ThenByDescending(booking => booking.Id),
            // `all` сохраняет прежний порядок и тай-брейк: внешние клиенты видят тот же список.
            _ => query
                .OrderByDescending(booking => booking.StartAt)
                .ThenBy(booking => booking.Id)
        };

        var rows = await ordered
            .Take(limit)
            .Select(booking => new BookingResponse(
                booking.Id,
                booking.UserId,
                booking.BarberProfileId,
                booking.BarberServiceId,
                booking.SalonId,
                booking.StartAt,
                booking.EndAt,
                booking.Price,
                booking.Status,
                booking.IdempotencyKey,
                booking.CustomerNote,
                booking.BarberNote,
                booking.CancellationReason,
                booking.CreatedAt,
                // EXISTS по уникальному ux_reviews_booking_id: отзыв один на бронь.
                Context.Reviews.Any(review => review.BookingId == booking.Id)))
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<Result<IReadOnlyList<BarberBookingResponse>, Failure>> GetBarberPageAsync(
        Guid barberProfileId,
        BarberBookingScope scope,
        DateTimeOffset now,
        BookingCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Booking.PageLimitInvalid,
                Messages.PAGE_LIMIT_NOT_POSITIVE,
                nameof(limit)));
        }

        var rows = await BuildBarberBookingsQuery(
                Context,
                barberProfileId,
                scope,
                now,
                cursor,
                limit)
            .ToListAsync(cancellationToken);

        return rows.Select(ToBarberResponse).ToList();
    }

    /// <summary>
    /// Собирает SQL списка записей барбера: левый join клиента, границы скоупа, keyset,
    /// порядок и проекцию в плоскую строку.
    /// </summary>
    /// <remarks>
    /// Вынесено из <see cref="GetBarberPageAsync"/>, чтобы unit-тест проверил трансляцию
    /// <c>users.avatar_url</c> через <c>ToQueryString()</c> без базы (AGENT.md §5.4).
    /// Соединения, условия обоих скоупов и порядок не менялись.
    /// </remarks>
    internal static IQueryable<BarberBookingRow> BuildBarberBookingsQuery(
        ApplicationDbContext context,
        Guid barberProfileId,
        BarberBookingScope scope,
        DateTimeOffset now,
        BookingCursor? cursor,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bookings = context.Bookings.AsNoTracking();

        // Навигаций между агрегатами в домене нет, поэтому клиент, услуга и салон
        // присоединяются явными join'ами по идентификаторам. Услуга и салон — внутренние:
        // без контекста визита строка бесполезна. Клиент — LEFT JOIN, потому что у гостя
        // user_id = NULL, и гостевая строка обязана остаться в кабинете барбера.
        var customers = context.Users
            .AsNoTracking()
            .Select(user => new
            {
                user.Id,
                user.FirstName,
                user.LastName,
                user.PhoneNumber,

                // Скрытый администратором или неодобренный аватар маскируется в SQL: барбер
                // видит инициалы, а строка записи остаётся. Маска — только для показа,
                // сама бронь не меняется.
                AvatarUrl = user.IsAvatarBlocked
                            || user.AvatarModerationStatus != PhotoModerationStatus.Approved
                    ? null
                    : user.AvatarUrl
            });

        var barberServices = context.BarberServices
            .AsNoTracking()
            .Select(service => new
            {
                service.Id,
                service.HairServiceId
            });

        var hairServices = context.HairServices
            .AsNoTracking()
            .Select(hairService => new
            {
                hairService.Id,
                hairService.Name
            });

        var salons = context.Salons
            .AsNoTracking()
            .Select(salon => new
            {
                salon.Id,
                salon.Name,
                salon.Address,
                salon.City,
                salon.TimeZoneId
            });

        // LEFT JOIN клиента: у гостевой брони (walk-in) user_id = NULL, и внутреннее
        // соединение выкинуло бы её из кабинета барбера. Гость остаётся в выдаче,
        // а имя и телефон берутся из гостевого снимка самой брони. Поля клиента
        // проецируются здесь же (паттерн левого соединения, как в админском журнале).
        var joined = from booking in bookings
                     join customer in customers
                         on booking.UserId equals (Guid?)customer.Id into customerJoin
                     from customer in customerJoin.DefaultIfEmpty()
                     join service in barberServices on booking.BarberServiceId equals service.Id
                     join hairService in hairServices on service.HairServiceId equals hairService.Id
                     join salon in salons on booking.SalonId equals salon.Id
                     where booking.BarberProfileId == barberProfileId
                     select new
                     {
                         booking,
                         CustomerFirstName = customer == null ? null : customer.FirstName,
                         CustomerLastName = customer == null ? null : customer.LastName,
                         CustomerPhoneNumber = customer == null ? null : customer.PhoneNumber,
                         CustomerAvatarUrl = customer == null ? null : customer.AvatarUrl,
                         hairService,
                         salon
                     };

        // Граница скоупа — EndAt, а не статус: активная бронь с прошедшим концом уже история,
        // а терминальный статус никогда не попадает в «ближайшие».
        joined = scope == BarberBookingScope.Upcoming
            ? joined.Where(row =>
                (row.booking.Status == BookingStatus.Pending
                 || row.booking.Status == BookingStatus.Confirmed)
                && row.booking.EndAt > now)
            : joined.Where(row =>
                (row.booking.Status != BookingStatus.Pending
                 && row.booking.Status != BookingStatus.Confirmed)
                || row.booking.EndAt <= now);

        // Направление keyset-сравнения задаёт скоуп: «ближайшие» идут вперёд,
        // история — назад, поэтому и курсор сравнивается в ту же сторону.
        if (cursor is { } position)
        {
            joined = scope == BarberBookingScope.Upcoming
                ? joined.Where(row =>
                    row.booking.StartAt > position.StartAt
                    || (row.booking.StartAt == position.StartAt
                        && row.booking.Id.CompareTo(position.Id) > 0))
                : joined.Where(row =>
                    row.booking.StartAt < position.StartAt
                    || (row.booking.StartAt == position.StartAt
                        && row.booking.Id.CompareTo(position.Id) < 0));
        }

        var ordered = scope == BarberBookingScope.Upcoming
            ? joined
                .OrderBy(row => row.booking.StartAt)
                .ThenBy(row => row.booking.Id)
            : joined
                .OrderByDescending(row => row.booking.StartAt)
                .ThenByDescending(row => row.booking.Id);

        return ordered
            .Take(limit)
            .Select(row => new BarberBookingRow(
                row.booking.Id,
                row.booking.Status,
                row.booking.StartAt,
                row.booking.EndAt,
                row.booking.Price,
                row.booking.CustomerNote,
                row.booking.BarberNote,
                row.booking.CancellationReason,
                row.CustomerFirstName,
                row.CustomerLastName,
                row.CustomerPhoneNumber,
                row.CustomerAvatarUrl,
                row.booking.GuestFirstName,
                row.booking.GuestLastName,
                row.booking.GuestPhone,
                row.hairService.Name,
                row.salon.Name,
                row.salon.Address,
                row.salon.City,
                row.salon.TimeZoneId));
    }

    public async Task<Result<IReadOnlyList<AdminBookingResponse>, Failure>> GetAdminPageAsync(
        AdminBookingFilters filters,
        AdminBookingCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);

        if (limit <= 0)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AdminBooking.PageLimitInvalid,
                Messages.PAGE_LIMIT_NOT_POSITIVE,
                nameof(limit)));
        }

        var rows = await BuildAdminBookingsQuery(Context, filters, cursor, limit)
            .ToListAsync(cancellationToken);

        return rows.Select(ToAdminResponse).ToList();
    }

    public async Task<Result<AdminBookingResponse, Failure>> GetAdminByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var filters = new AdminBookingFilters(Status: null, Search: null);

        var row = await BuildAdminBookingsQuery(
                Context,
                filters,
                cursor: null,
                limit: null,
                bookingId: id)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return new Failure(Error.NotFound(
                ApiErrorCodes.AdminBooking.NotFound,
                $"Booking '{id}' was not found.",
                id));
        }

        return ToAdminResponse(row);
    }

    /// <summary>
    /// Сколько броней подтверждено — ровно фильтр «Подтверждена» админского списка.
    /// </summary>
    /// <remarks>
    /// Предикат <c>status = Confirmed</c> — тот же, что у <see cref="GetAdminPageAsync"/>
    /// с фильтром <c>Confirmed</c>: число в сводке обязано совпадать с размером раздела.
    /// Прошедшие визиты тоже считаются: ограничение «только предстоящие» разошлось бы
    /// с фильтром. Это контракт, а не деталь реализации.
    /// </remarks>
    public async Task<Result<int, Failure>> CountConfirmedAsync(
        CancellationToken cancellationToken = default)
    {
        var count = await Context.Bookings
            .AsNoTracking()
            .CountAsync(
                booking => booking.Status == BookingStatus.Confirmed,
                cancellationToken);

        return Result.Success<int, Failure>(count);
    }

    /// <summary>
    /// Собирает SQL админского чтения броней: соединения, фильтры, keyset, порядок
    /// и проекцию в плоскую строку.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Барбер, услуга и салон присоединяются внутренними соединениями: историческая строка
    /// с оборванной ссылкой на них выпадает из админского списка — карточка без контекста
    /// визита бесполезна. Клиент — <c>LEFT JOIN</c>: у гостевой брони (walk-in)
    /// <c>user_id = NULL</c>, и внутреннее соединение скрыло бы её от администратора.
    /// </para>
    /// <para>
    /// Поиск: строка, разбирающаяся как <see cref="Guid"/>, — точный идентификатор брони;
    /// иначе подстрока email клиента через <c>ILIKE</c> с экранированными <c>%</c>, <c>_</c>
    /// и <c>\</c>. У гостя email нет, поэтому подстрокой он не находится, но в
    /// нефильтрованном списке остаётся. По имени и телефону клиента, email барбера
    /// и префиксу id поиск не выполняется. Порядок — <c>start_at DESC, id ASC</c>;
    /// второй ключ обязателен, потому что <c>start_at</c> не уникален.
    /// </para>
    /// </remarks>
    internal static IQueryable<AdminBookingRow> BuildAdminBookingsQuery(
        ApplicationDbContext context,
        AdminBookingFilters filters,
        AdminBookingCursor? cursor,
        int? limit,
        Guid? bookingId = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(filters);

        var bookings = context.Bookings.AsNoTracking();

        // Навигаций между агрегатами в домене нет, поэтому клиент, барбер, услуга и салон
        // присоединяются явными join'ами по идентификаторам.
        var customers = context.Users
            .AsNoTracking()
            .Select(user => new
            {
                user.Id,
                user.FirstName,
                user.LastName,
                user.Email,
                user.PhoneNumber,
                user.AvatarUrl
            });

        var barberProfiles = context.BarberProfiles
            .AsNoTracking()
            .Select(profile => new
            {
                profile.Id,
                profile.UserId,
                DisplayName = profile.DisplayName.Value
            });

        // Аватар барбера живёт в users по barber_profiles.user_id; это отдельный join
        // от клиентского: тот соединяется по bookings.user_id, а не по владельцу профиля.
        var barberUsers = context.Users
            .AsNoTracking()
            .Select(user => new
            {
                user.Id,
                user.AvatarUrl
            });

        var barberServices = context.BarberServices
            .AsNoTracking()
            .Select(service => new
            {
                service.Id,
                service.HairServiceId
            });

        var hairServices = context.HairServices
            .AsNoTracking()
            .Select(hairService => new
            {
                hairService.Id,
                hairService.Name
            });

        var salons = context.Salons
            .AsNoTracking()
            .Select(salon => new
            {
                salon.Id,
                salon.Name,
                salon.Address,
                salon.City,
                salon.TimeZoneId
            });

        // LEFT JOIN клиента: гостевая бронь (walk-in) не имеет user_id, но обязана остаться
        // видимой администратору. Услуга, барбер и салон по-прежнему внутренние: это
        // обязательный контекст визита.
        var joined = from booking in bookings
                     join customer in customers
                         on booking.UserId equals (Guid?)customer.Id into customerJoin
                     from customer in customerJoin.DefaultIfEmpty()
                     join barberProfile in barberProfiles
                         on booking.BarberProfileId equals barberProfile.Id
                     join barberUser in barberUsers
                         on barberProfile.UserId equals barberUser.Id into barberUserJoin
                     from barberUser in barberUserJoin.DefaultIfEmpty()
                     join service in barberServices on booking.BarberServiceId equals service.Id
                     join hairService in hairServices
                         on service.HairServiceId equals hairService.Id
                     join salon in salons on booking.SalonId equals salon.Id
                     select new
                     {
                         booking,
                         CustomerId = customer == null ? null : (Guid?)customer.Id,
                         CustomerFirstName = customer == null ? null : customer.FirstName,
                         CustomerLastName = customer == null ? null : customer.LastName,
                         CustomerEmail = customer == null ? null : customer.Email,
                         CustomerPhoneNumber = customer == null ? null : customer.PhoneNumber,
                         CustomerAvatarUrl = customer == null ? null : customer.AvatarUrl,
                         barberProfile,
                         BarberAvatarUrl = barberUser == null ? null : barberUser.AvatarUrl,
                         hairService,
                         salon
                     };

        if (filters.Status is { } status)
        {
            joined = joined.Where(row => row.booking.Status == status);
        }

        if (bookingId is { } id)
        {
            joined = joined.Where(row => row.booking.Id == id);
        }

        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var search = filters.Search;

            if (Guid.TryParse(search, out var searchedId))
            {
                // GUID — это идентификатор брони, а не подстрока email: искать «%guid%»
                // по адресу бессмысленно.
                joined = joined.Where(row => row.booking.Id == searchedId);
            }
            else
            {
                var pattern = LikePattern.Contains(search);

                // Поиск остаётся по email клиента: у гостя email нет, поэтому подстрокой
                // он не находится, но в нефильтрованном списке остаётся видимым.
                // Условие смотрит на уже спроецированное поле: проверка `customer != null`
                // на анонимном типе после DefaultIfEmpty не транслируется в SQL.
                joined = joined.Where(row =>
                    row.CustomerEmail != null
                    && EF.Functions.ILike(row.CustomerEmail, pattern, "\\"));
            }
        }

        if (cursor is { } position)
        {
            joined = joined.Where(row =>
                row.booking.StartAt < position.StartAt
                || (row.booking.StartAt == position.StartAt
                    && row.booking.Id.CompareTo(position.Id) > 0));
        }

        var ordered = joined
            .OrderByDescending(row => row.booking.StartAt)
            .ThenBy(row => row.booking.Id);

        var paged = limit is { } take ? ordered.Take(take) : ordered;

        return paged.Select(row => new AdminBookingRow(
            row.booking.Id,
            row.booking.Status,
            row.booking.StartAt,
            row.booking.EndAt,
            row.booking.Price,
            row.booking.CustomerNote,
            row.booking.BarberNote,
            row.booking.CancellationReason,
            row.booking.CreatedAt,
            row.CustomerId,
            row.CustomerFirstName,
            row.CustomerLastName,
            row.CustomerEmail,
            row.CustomerPhoneNumber,
            row.CustomerAvatarUrl,
            row.booking.GuestFirstName,
            row.booking.GuestLastName,
            row.booking.GuestPhone,
            row.barberProfile.Id,
            row.barberProfile.DisplayName,
            row.BarberAvatarUrl,
            row.hairService.Name,
            row.salon.Id,
            row.salon.Name,
            row.salon.Address,
            row.salon.City,
            row.salon.TimeZoneId));
    }

    /// <summary>
    /// Собирает админский контракт из плоской строки.
    /// </summary>
    /// <remarks>
    /// Вложенные записи конструируются в памяти, чтобы EF не собирал объектную модель в SQL,
    /// — тот же приём, что в админском чтении отзывов и журнала.
    /// </remarks>
    private static AdminBookingResponse ToAdminResponse(AdminBookingRow row)
        => new(
            row.BookingId,
            row.Status,
            row.StartAt,
            row.EndAt,
            row.Price,
            row.CustomerNote,
            row.BarberNote,
            row.CancellationReason,
            row.CreatedAt,
            new AdminBookingCustomerResponse(
                row.CustomerId,
                // У гостя зарегистрированных полей нет: имя и телефон берутся из снимка брони.
                row.CustomerFirstName ?? row.GuestFirstName ?? string.Empty,
                row.CustomerLastName ?? row.GuestLastName ?? string.Empty,
                row.CustomerEmail,
                row.CustomerPhoneNumber ?? row.GuestPhone,
                row.CustomerAvatarUrl),
            new AdminBookingBarberResponse(
                row.BarberProfileId,
                row.DisplayName,
                row.BarberAvatarUrl),
            new AdminBookingServiceResponse(row.ServiceName),
            new AdminBookingSalonResponse(
                row.SalonId,
                row.SalonName,
                row.SalonAddress,
                row.SalonCity,
                row.TimeZoneId));

    public async Task<Result<IReadOnlyList<Booking>, Failure>> GetActiveByBarberAsync(
        Guid barberProfileId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Booking.ActivePeriodInvalid,
                "The end of the period must be after its start.",
                nameof(to)));
        }

        var bookings = await Context.Bookings
            .AsNoTracking()
            // Отменённые, завершённые и неявившиеся слот не занимают: тот же фильтр,
            // что и в частичном индексе ix_bookings_barber_profile_id_start_at_active.
            .Where(x => x.BarberProfileId == barberProfileId
                        && (x.Status == BookingStatus.Pending || x.Status == BookingStatus.Confirmed)
                        && x.StartAt < to
                        && from < x.EndAt)
            .OrderBy(x => x.StartAt)
            .ToListAsync(cancellationToken);

        return bookings;
    }

    public async Task<Result<bool, Failure>> HasActiveOverlapAsync(
        Guid barberProfileId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        CancellationToken cancellationToken = default)
    {
        if (endAt <= startAt)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Booking.OverlapPeriodInvalid,
                DomainMessages.END_TIME_NOT_AFTER_START,
                nameof(endAt)));
        }

        var overlaps = await Context.Bookings
            .AsNoTracking()
            .AnyAsync(
                x => x.BarberProfileId == barberProfileId
                     && (x.Status == BookingStatus.Pending || x.Status == BookingStatus.Confirmed)
                     && x.StartAt < endAt
                     && startAt < x.EndAt,
                cancellationToken);

        return overlaps;
    }

    public async Task<UnitResult<Failure>> AcquireBarberLockAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
    {
        // 50 concurrent inserts on one slot make PostgreSQL's GiST exclusion check
        // deadlock (40P01) before either transaction can commit, and under load all
        // contenders can lose, so no 201 is produced at all. Serialising inserts for
        // one barber with a transaction-scoped advisory lock makes the exclusion
        // constraint the only decider: first insert wins, the rest get 23P01 -> 409.
        // Booking create is low-volume (§15.1), so per-barber serialisation is cheap.
        //
        // Lock lives until the end of the caller's transaction, so this method must be
        // called inside IUnitOfWork.ExecuteAsync. It is deliberately narrow: the
        // repository neither starts nor commits the transaction (R-15).
        //
        // SET LOCAL действует только до конца текущей транзакции и только на это
        // соединение, поэтому read-запросы без транзакции его не получают. Без
        // ограничения pg_advisory_xact_lock ждал бы бесконечно: зависшая транзакция
        // держала бы все новые брони барбера и копила запросы в пуле (R-18).
        try
        {
            await Context.Database.ExecuteSqlRawAsync(
                $"SET LOCAL lock_timeout = '{PostgresSettings.LOCK_TIMEOUT}'",
                cancellationToken);

            await Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 SELECT pg_advisory_xact_lock(
                     hashtextextended({barberProfileId.ToString()}, 0))
                 """,
                cancellationToken);

            return UnitResult.Success<Failure>();
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            // 55P03: блокировку не удалось взять за lock_timeout. Транзакция уже
            // прервана, её откатит IUnitOfWork; клиенту отдаём 409, а не 500.
            return UnitResult.Failure(MapPostgresError(exception, ApiErrorCodes.Booking.AddPrefix));
        }
    }

    public async Task<UnitResult<Failure>> AddAsync(
        Booking booking,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(booking);

        try
        {
            await Context.Bookings.AddAsync(booking, cancellationToken);

            // Повтор ключа идемпотентности ловит уникальный индекс
            // ux_bookings_user_id_idempotency_key, пересечение интервалов — exclusion-ограничение
            // ex_bookings_no_overlap. Оба случая маппятся в Error.Conflict.
            //
            // Транзакцию репозиторий не открывает: её границей владеет IUnitOfWork,
            // поэтому вставка не может закоммититься раньше проверок обработчика.
            var result = await SaveChangesAsync(ApiErrorCodes.Booking.AddPrefix, cancellationToken);

            if (result.IsFailure)
            {
                // Неудачная вставка не должна остаться в change tracker: обработчик затем
                // перечитывает бронь по ключу, а случайный повторный SaveChanges в том же
                // запросе снова наткнулся бы на то же ограничение.
                Context.Entry(booking).State = EntityState.Detached;
            }

            return result;
        }
        catch
        {
            // Исключение уже откатит транзакцию IUnitOfWork; здесь важно только не оставить
            // бронь в трекере, иначе она может быть записана следующим SaveChanges.
            Context.Entry(booking).State = EntityState.Detached;

            throw;
        }
    }

    public async Task<UnitResult<Failure>> UpdateAsync(
        Booking booking,
        CancellationToken cancellationToken = default)
        => await SaveGraphAsync(booking, ApiErrorCodes.Booking.UpdatePrefix, cancellationToken);

    /// <summary>
    /// Собирает вложенные response-записи из плоской строки и нормализует имя клиента.
    /// </summary>
    /// <remarks>
    /// Зарегистрированный клиент и гость взаимоисключающие, поэтому имя и телефон берутся
    /// из первых заполненных полей: у гостя зарегистрированных нет, у клиента — гостевых.
    /// </remarks>
    private static BarberBookingResponse ToBarberResponse(BarberBookingRow row)
    {
        var firstName = row.CustomerFirstName ?? row.GuestFirstName;
        var lastName = row.CustomerLastName ?? row.GuestLastName;
        var phone = row.CustomerPhoneNumber ?? row.GuestPhone;

        return new BarberBookingResponse(
            row.Id,
            row.Status,
            row.StartAt,
            row.EndAt,
            row.Price,
            row.CustomerNote,
            row.BarberNote,
            row.CancellationReason,
            new BarberBookingCustomerResponse(
                BuildCustomerName(firstName, lastName),
                string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
                row.CustomerAvatarUrl),
            new BarberBookingServiceResponse(row.ServiceName),
            new BarberBookingSalonResponse(
                row.SalonName,
                row.SalonAddress,
                row.SalonCity,
                row.TimeZoneId));
    }

    /// <summary>
    /// Имя клиента для списка: срезает края и схлопывает повторные пробелы.
    /// </summary>
    /// <remarks>
    /// Домен не нормализует имя зарегистрированного пользователя, поэтому «Иван  Петров»
    /// и «Иван Петров» могут лежать в базе по-разному. Для строки списка нужен один вид —
    /// иначе вёрстка прыгает. У гостя имя уже нормализовано доменом, но пустые части
    /// (<c>null</c>) обрабатываются так же.
    /// </remarks>
    private static string BuildCustomerName(string? firstName, string? lastName)
    {
        var name = $"{firstName} {lastName}".Trim();

        return name.Contains("  ", StringComparison.Ordinal)
            ? string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            : name;
    }

    /// <summary>
    /// Плоская строка чтения броней барбера до сборки вложенных response-записей.
    /// </summary>
    /// <remarks>
    /// Вложенные записи конструируются в памяти, чтобы EF не собирал объектную модель в SQL,
    /// — тот же приём, что в админском чтении отзывов.
    /// </remarks>
    internal sealed record BarberBookingRow(
        Guid Id,
        BookingStatus Status,
        DateTimeOffset StartAt,
        DateTimeOffset EndAt,
        decimal Price,
        string? CustomerNote,
        string? BarberNote,
        string? CancellationReason,
        string? CustomerFirstName,
        string? CustomerLastName,
        string? CustomerPhoneNumber,
        string? CustomerAvatarUrl,
        string? GuestFirstName,
        string? GuestLastName,
        string? GuestPhone,
        string ServiceName,
        string SalonName,
        string SalonAddress,
        string SalonCity,
        string TimeZoneId);

    /// <summary>
    /// Плоская строка админского чтения броней до сборки вложенных response-записей.
    /// </summary>
    /// <remarks>
    /// Сущность <see cref="Booking"/> не покидает репозиторий: наружу уходит контракт
    /// <see cref="AdminBookingResponse"/>, собранный из этой строки в памяти.
    /// </remarks>
    internal sealed record AdminBookingRow(
        Guid BookingId,
        BookingStatus Status,
        DateTimeOffset StartAt,
        DateTimeOffset EndAt,
        decimal Price,
        string? CustomerNote,
        string? BarberNote,
        string? CancellationReason,
        DateTimeOffset CreatedAt,
        Guid? CustomerId,
        string? CustomerFirstName,
        string? CustomerLastName,
        string? CustomerEmail,
        string? CustomerPhoneNumber,
        string? CustomerAvatarUrl,
        string? GuestFirstName,
        string? GuestLastName,
        string? GuestPhone,
        Guid BarberProfileId,
        string DisplayName,
        string? BarberAvatarUrl,
        string ServiceName,
        Guid SalonId,
        string SalonName,
        string SalonAddress,
        string SalonCity,
        string TimeZoneId);
}
