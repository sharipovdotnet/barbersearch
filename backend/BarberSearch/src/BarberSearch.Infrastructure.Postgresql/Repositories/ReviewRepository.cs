using BarberSearch.Application.Common;
using BarberSearch.Application.Reviews;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.Admin;
using BarberSearch.Contracts.Reviews.GetMyReviews;
using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Domain.Reviews;
using BarberSearch.Infrastructure.Postgresql.Audit;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий агрегата <see cref="Review"/>.
/// </summary>
public sealed class ReviewRepository : RepositoryBase<Review>, IReviewRepository
{
    public ReviewRepository(ApplicationDbContext context, AuditTrail? auditTrail = null)
        : base(context, auditTrail)
    {
    }

    public async Task<Result<Review, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var review = await Context.Reviews
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (review is null)
        {
            return NotFound(ApiErrorCodes.Review.NotFound, nameof(Review), id);
        }

        return review;
    }

    public async Task<Result<Review, Failure>> GetByBookingIdAsync(
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        var review = await Context.Reviews
            .FirstOrDefaultAsync(x => x.BookingId == bookingId, cancellationToken);

        if (review is null)
        {
            return new Failure(Error.NotFound(
                ApiErrorCodes.Review.NotFound,
                $"Review for booking '{bookingId}' was not found.",
                bookingId));
        }

        return review;
    }

    /// <summary>
    /// Страница отзывов барбера, свежие сверху. Читается без отслеживания.
    /// </summary>
    /// <remarks>
    /// Глубина offset-выдачи ограничена: <c>page * pageSize</c> не может превышать
    /// <see cref="MAX_PAGE_OFFSET"/> (10 000), иначе <c>400</c> с кодом
    /// <c>review.paging.depth</c> — до обращения к базе.
    /// </remarks>
    public async Task<Result<IReadOnlyList<Review>, Failure>> GetByBarberProfileIdAsync(
        Guid barberProfileId,
        int page,
        int pageSize,
        bool onlyPublished = true,
        CancellationToken cancellationToken = default)
    {
        if (ValidatePaging(
                page,
                pageSize,
                ApiErrorCodes.Review.PagingPageInvalid,
                ApiErrorCodes.Review.PagingPageSizeInvalid) is { } pagingError)
        {
            return pagingError;
        }

        if (ValidatePageOffset(
                page,
                pageSize,
                ApiErrorCodes.Review.PagingDepthInvalid) is { } depthError)
        {
            return depthError;
        }

        var query = Context.Reviews
            .AsNoTracking()
            .Where(x => x.BarberProfileId == barberProfileId);

        if (onlyPublished)
        {
            query = query.Where(x => x.IsPublished);
        }

        // План индекса ix_reviews_barber_profile_id_created_at: свежие сверху.
        var reviews = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return reviews;
    }

    public async Task<Result<IReadOnlyList<ReviewResponse>, Failure>> GetPublishedPageAsync(
        Guid barberProfileId,
        ReviewCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Review.PageLimitInvalid,
                Messages.PAGE_LIMIT_NOT_POSITIVE,
                nameof(limit)));
        }

        var query = Context.Reviews
            .AsNoTracking()
            .Where(review => review.BarberProfileId == barberProfileId && review.IsPublished);

        if (cursor is { } position)
        {
            query = query.Where(review =>
                review.CreatedAt < position.CreatedAt
                || (review.CreatedAt == position.CreatedAt
                    && review.Id.CompareTo(position.Id) > 0));
        }

        var rows = await query
            .OrderByDescending(review => review.CreatedAt)
            .ThenBy(review => review.Id)
            .Take(limit)
            .Select(review => new ReviewResponse(
                review.Id,
                review.BarberProfileId,
                review.UserId,
                review.BookingId,
                review.Rating,
                review.Text,
                review.IsPublished,
                review.CreatedAt,
                review.UpdatedAt))
            .ToListAsync(cancellationToken);

        return rows;
    }

    /// <summary>
    /// Очередь модерации: скрытые отзывы всех барберов, старые сверху (FIFO).
    /// </summary>
    /// <remarks>
    /// Порядок <c>created_at ASC, id</c> обслуживает частичный индекс
    /// <c>ix_reviews_moderation_created_at_id</c> (<c>WHERE is_published = false</c>).
    /// Курсор хранит пару <c>(created_at, id)</c>: условие продолжения обратно предикату
    /// карточки, потому что очередь читается по возрастанию. Опубликованные отзывы в выдачу
    /// не попадают. <paramref name="limit"/> &lt;= 0 даёт <c>400 review.page.limit</c> до SQL.
    /// </remarks>
    public async Task<Result<IReadOnlyList<ReviewResponse>, Failure>> GetModerationPageAsync(
        ReviewCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Review.PageLimitInvalid,
                Messages.PAGE_LIMIT_NOT_POSITIVE,
                nameof(limit)));
        }

        var query = Context.Reviews
            .AsNoTracking()
            .Where(review => !review.IsPublished);

        if (cursor is { } position)
        {
            query = query.Where(review =>
                review.CreatedAt > position.CreatedAt
                || (review.CreatedAt == position.CreatedAt
                    && review.Id.CompareTo(position.Id) > 0));
        }

        var rows = await query
            .OrderBy(review => review.CreatedAt)
            .ThenBy(review => review.Id)
            .Take(limit)
            .Select(review => new ReviewResponse(
                review.Id,
                review.BarberProfileId,
                review.UserId,
                review.BookingId,
                review.Rating,
                review.Text,
                review.IsPublished,
                review.CreatedAt,
                review.UpdatedAt))
            .ToListAsync(cancellationToken);

        return rows;
    }

    /// <summary>
    /// Страница «мои отзывы» — отзывы одного автора в любом статусе, свежие сверху.
    /// </summary>
    /// <remarks>
    /// Автор фильтруется по <c>user_id</c>, статус не фильтруется: кабинету нужны и скрытые,
    /// и опубликованные отзывы. Порядок <c>created_at DESC, id</c> обслуживает индекс
    /// <c>ix_reviews_user_id_created_at_id</c>. Контекст визита собирается join'ами,
    /// автор в проекцию не входит — это сам вызывающий. <paramref name="limit"/> &lt;= 0
    /// даёт <c>400 review.page.limit</c> до SQL.
    /// </remarks>
    public async Task<Result<IReadOnlyList<MyReviewResponse>, Failure>> GetMyPageAsync(
        Guid userId,
        ReviewCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Review.PageLimitInvalid,
                Messages.PAGE_LIMIT_NOT_POSITIVE,
                nameof(limit)));
        }

        var rows = await BuildMyReviewsQuery(Context, userId, cursor, limit)
            .ToListAsync(cancellationToken);

        return rows.Select(ToMyResponse).ToList();
    }

    /// <summary>
    /// Общая проекция списка «мои отзывы»: отзыв и контекст его визита, без автора.
    /// </summary>
    /// <remarks>
    /// Join'ы внутренние, как в админской проекции: деактивированные профили, услуги и салоны
    /// не удаляются, поэтому потерявших контекст строк не бывает, а fallback-имена не нужны.
    /// Проекция плоская: вложенные response-записи конструируются в памяти, чтобы EF не собирал
    /// объектную модель в SQL.
    /// </remarks>
    /// <param name="context">Контекст базы данных.</param>
    /// <param name="userId">Автор отзыва.</param>
    /// <param name="cursor">Позиция продолжения или <c>null</c> для первой страницы.</param>
    /// <param name="limit">Сколько строк прочитать; <c>null</c> — без ограничения.</param>
    internal static IQueryable<MyReviewRow> BuildMyReviewsQuery(
        ApplicationDbContext context,
        Guid userId,
        ReviewCursor? cursor = null,
        int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var reviews = context.Reviews
            .AsNoTracking()
            .Where(review => review.UserId == userId);

        var bookings = context.Bookings.AsNoTracking();

        var barbers = context.BarberProfiles
            .AsNoTracking()
            .Select(profile => new
            {
                profile.Id,
                DisplayName = profile.DisplayName.Value
            });

        var services = context.BarberServices
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
                salon.TimeZoneId
            });

        var joined = from review in reviews
                     join booking in bookings on review.BookingId equals booking.Id
                     join barber in barbers on review.BarberProfileId equals barber.Id
                     join service in services on booking.BarberServiceId equals service.Id
                     join hairService in hairServices on service.HairServiceId equals hairService.Id
                     join salon in salons on booking.SalonId equals salon.Id
                     select new
                     {
                         review,
                         booking,
                         barber,
                         hairService,
                         salon
                     };

        if (cursor is { } position)
        {
            joined = joined.Where(row =>
                row.review.CreatedAt < position.CreatedAt
                || (row.review.CreatedAt == position.CreatedAt
                    && row.review.Id.CompareTo(position.Id) > 0));
        }

        var ordered = joined
            .OrderByDescending(row => row.review.CreatedAt)
            .ThenBy(row => row.review.Id);

        var paged = limit is { } take ? ordered.Take(take) : ordered;

        return paged.Select(row => new MyReviewRow(
            row.review.Id,
            row.review.Rating,
            row.review.Text,
            row.review.IsPublished,
            row.review.CreatedAt,
            row.review.UpdatedAt,
            row.barber.Id,
            row.barber.DisplayName,
            row.booking.Id,
            row.booking.StartAt,
            row.salon.TimeZoneId,
            row.booking.Price,
            row.hairService.Name,
            row.salon.Name));
    }

    private static MyReviewResponse ToMyResponse(MyReviewRow row)
        => new(
            row.Id,
            row.Rating,
            row.Text,
            row.IsPublished,
            row.CreatedAt,
            row.UpdatedAt,
            new MyReviewBarberResponse(row.BarberId, row.BarberDisplayName),
            new MyReviewVisitResponse(
                row.BookingId,
                row.StartAt,
                row.TimeZoneId,
                row.Price,
                row.ServiceName,
                row.SalonName));

    /// <summary>
    /// Страница отзывов для админского экрана: скрытые (FIFO) или опубликованные (свежие сверху).
    /// </summary>
    /// <remarks>
    /// Проекция собирает имя барбера, клиента и контекст визита из соседних агрегатов.
    /// Это единственное чтение отзывов, которое отдаёт PII, поэтому оно закрыто
    /// <c>AdminOnly</c> на контроллере. Фильтр и порядок задаёт <paramref name="isPublished"/>:
    /// для очереди работает частичный индекс <c>ix_reviews_moderation_created_at_id</c>,
    /// для опубликованных — <c>ix_reviews_published_created_at_id</c>.
    /// </remarks>
    public async Task<Result<IReadOnlyList<AdminReviewResponse>, Failure>> GetAdminPageAsync(
        bool isPublished,
        ReviewCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Review.PageLimitInvalid,
                Messages.PAGE_LIMIT_NOT_POSITIVE,
                nameof(limit)));
        }

        var rows = await BuildAdminReviewsQuery(
                Context,
                isPublished: isPublished,
                cursor: cursor,
                descending: isPublished,
                limit: limit)
            .ToListAsync(cancellationToken);

        return rows.Select(ToAdminResponse).ToList();
    }

    /// <summary>
    /// Один отзыв любого статуса для админской карточки.
    /// </summary>
    public async Task<Result<AdminReviewResponse, Failure>> GetAdminByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var row = await BuildAdminReviewsQuery(Context, reviewId: id)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return new Failure(Error.NotFound(
                ApiErrorCodes.Review.NotFound,
                $"Review '{id}' was not found.",
                id));
        }

        return ToAdminResponse(row);
    }

    /// <summary>
    /// Общая проекция админского чтения отзыва: отзыв, визит, барбер, клиент, услуга и салон.
    /// </summary>
    /// <remarks>
    /// Навигаций между агрегатами в домене нет, поэтому связи собираются явными join'ами
    /// по идентификаторам. Проекция плоская: вложенные response-записи конструируются
    /// в памяти, чтобы EF не собирал объектную модель в SQL.
    /// </remarks>
    internal static IQueryable<AdminReviewRow> BuildAdminReviewsQuery(
        ApplicationDbContext context,
        bool? isPublished = null,
        Guid? reviewId = null,
        ReviewCursor? cursor = null,
        bool descending = false,
        int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var reviews = context.Reviews.AsNoTracking();
        var bookings = context.Bookings.AsNoTracking();

        var customers = context.Users
            .AsNoTracking()
            .Select(user => new
            {
                user.Id,
                user.FirstName,
                user.LastName,
                user.Email
            });

        var barbers = context.BarberProfiles
            .AsNoTracking()
            .Select(profile => new
            {
                profile.Id,
                DisplayName = profile.DisplayName.Value,
                profile.Rating,
                profile.ReviewsCount,
                profile.IsActive,
                profile.IsVerified
            });

        var services = context.BarberServices
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
                salon.TimeZoneId
            });

        var joined = from review in reviews
                     join booking in bookings on review.BookingId equals booking.Id
                     join customer in customers on review.UserId equals customer.Id
                     join barber in barbers on review.BarberProfileId equals barber.Id
                     join service in services on booking.BarberServiceId equals service.Id
                     join hairService in hairServices on service.HairServiceId equals hairService.Id
                     join salon in salons on booking.SalonId equals salon.Id
                     select new
                     {
                         review,
                         booking,
                         customer,
                         barber,
                         hairService,
                         salon
                     };

        // Фильтры и порядок применяются до проекции в запись: EF не транслирует
        // Where/OrderBy поверх конструктора AdminReviewRow.
        if (reviewId is { } id)
        {
            joined = joined.Where(row => row.review.Id == id);
        }

        if (isPublished is { } published)
        {
            joined = joined.Where(row => row.review.IsPublished == published);
        }

        if (cursor is { } position)
        {
            joined = descending
                ? joined.Where(row =>
                    row.review.CreatedAt < position.CreatedAt
                    || (row.review.CreatedAt == position.CreatedAt
                        && row.review.Id.CompareTo(position.Id) > 0))
                : joined.Where(row =>
                    row.review.CreatedAt > position.CreatedAt
                    || (row.review.CreatedAt == position.CreatedAt
                        && row.review.Id.CompareTo(position.Id) > 0));
        }

        var ordered = descending
            ? joined.OrderByDescending(row => row.review.CreatedAt).ThenBy(row => row.review.Id)
            : joined.OrderBy(row => row.review.CreatedAt).ThenBy(row => row.review.Id);

        var paged = limit is { } take ? ordered.Take(take) : ordered;

        return paged.Select(row => new AdminReviewRow(
            row.review.Id,
            row.review.Rating,
            row.review.Text,
            row.review.IsPublished,
            row.review.CreatedAt,
            row.review.UpdatedAt,
            row.barber.Id,
            row.barber.DisplayName,
            row.barber.Rating,
            row.barber.ReviewsCount,
            row.barber.IsActive,
            row.barber.IsVerified,
            row.customer.Id,
            row.customer.FirstName,
            row.customer.LastName,
            row.customer.Email,
            row.booking.Id,
            row.booking.StartAt,
            row.booking.Price,
            row.hairService.Name,
            row.salon.Name,
            row.salon.TimeZoneId));
    }

    private static AdminReviewResponse ToAdminResponse(AdminReviewRow row)
    {
        var fullName = $"{row.CustomerFirstName} {row.CustomerLastName}".Trim();

        return new AdminReviewResponse(
            row.Id,
            row.Rating,
            row.Text,
            row.IsPublished,
            row.CreatedAt,
            row.UpdatedAt,
            new AdminReviewBarberResponse(
                row.BarberId,
                row.BarberDisplayName,
                row.BarberRating,
                row.BarberReviewsCount,
                row.BarberIsActive,
                row.BarberIsVerified),
            new AdminReviewCustomerResponse(
                row.CustomerId,
                string.IsNullOrWhiteSpace(fullName) ? row.CustomerEmail : fullName,
                row.CustomerEmail),
            new AdminReviewVisitResponse(
                row.BookingId,
                row.StartAt,
                row.Price,
                row.ServiceName,
                row.SalonName,
                row.TimeZoneId));
    }

    public async Task<Result<ReviewRatingSummary, Failure>> GetRatingSummaryAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
    {
        var summary = await BuildRatingSummaryQuery(Context, barberProfileId)
            .FirstOrDefaultAsync(cancellationToken);

        if (summary is null || summary.Count == 0)
        {
            return new ReviewRatingSummary(null, 0);
        }

        return summary;
    }

    /// <summary>
    /// Строит один агрегирующий запрос сводки по опубликованным отзывам.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CountAsync</c> + <c>AverageAsync</c> — это два round-trip к <c>reviews</c>.
    /// Сводка читается, пока <c>ReviewRatingRecalculator</c> держит
    /// <c>SELECT … FOR UPDATE</c> на строке профиля, поэтому второй запрос удлиняет
    /// блокировку; здесь <c>COUNT</c> и <c>AVG</c> возвращаются одним SQL.
    /// </para>
    /// <para>
    /// Пустая выборка не даёт строки, и <c>FirstOrDefaultAsync</c> возвращает <c>null</c>;
    /// округление среднего по-прежнему делает домен (<c>BarberProfile.UpdateRating</c>).
    /// </para>
    /// </remarks>
    /// <param name="context">Контекст базы данных.</param>
    /// <param name="barberProfileId">Профиль, чьи отзывы агрегируются.</param>
    /// <returns>Запрос, материализующий сводку либо <c>null</c> при отсутствии отзывов.</returns>
    internal static IQueryable<ReviewRatingSummary> BuildRatingSummaryQuery(
        ApplicationDbContext context,
        Guid barberProfileId)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Reviews
            .AsNoTracking()
            .Where(review => review.BarberProfileId == barberProfileId && review.IsPublished)
            .GroupBy(_ => 1)
            .Select(group => new ReviewRatingSummary(
                group.Average(review => (decimal?)review.Rating),
                group.Count()));
    }

    public async Task<UnitResult<Failure>> AddAsync(
        Review review,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);

        await Context.Reviews.AddAsync(review, cancellationToken);

        // Второй отзыв на ту же бронь поймает уникальный индекс ux_reviews_booking_id.
        return await SaveChangesAsync(ApiErrorCodes.Review.AddPrefix, cancellationToken);
    }

    public async Task<UnitResult<Failure>> UpdateAsync(
        Review review,
        CancellationToken cancellationToken = default)
        => await SaveGraphAsync(review, ApiErrorCodes.Review.UpdatePrefix, cancellationToken);

    public async Task<UnitResult<Failure>> DeleteAsync(
        Review review,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);

        Context.Reviews.Remove(review);

        return await SaveChangesAsync(ApiErrorCodes.Review.DeletePrefix, cancellationToken);
    }

    /// <summary>
    /// Плоская строка админского чтения до сборки вложенных response-записей.
    /// </summary>
    internal sealed record AdminReviewRow(
        Guid Id,
        int Rating,
        string? Text,
        bool IsPublished,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        Guid BarberId,
        string BarberDisplayName,
        decimal? BarberRating,
        int BarberReviewsCount,
        bool BarberIsActive,
        bool BarberIsVerified,
        Guid CustomerId,
        string CustomerFirstName,
        string CustomerLastName,
        string CustomerEmail,
        Guid BookingId,
        DateTimeOffset StartAt,
        decimal Price,
        string ServiceName,
        string SalonName,
        string TimeZoneId);

    /// <summary>
    /// Плоская строка списка «мои отзывы»: отзыв и контекст визита, без автора.
    /// </summary>
    internal sealed record MyReviewRow(
        Guid Id,
        int Rating,
        string? Text,
        bool IsPublished,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        Guid BarberId,
        string BarberDisplayName,
        Guid BookingId,
        DateTimeOffset StartAt,
        string TimeZoneId,
        decimal Price,
        string ServiceName,
        string SalonName);
}
