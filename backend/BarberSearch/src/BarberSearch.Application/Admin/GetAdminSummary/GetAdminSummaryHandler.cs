using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.BarberRequests;
using BarberSearch.Application.Bookings;
using BarberSearch.Application.Reviews;
using BarberSearch.Application.Salons;
using BarberSearch.Application.Users;
using BarberSearch.Contracts.Admin;
using BarberSearch.Contracts.Reviews.Admin;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Admin.GetAdminSummary;

/// <summary>
/// Обработчик админской сводки: счётчики очередей разделов и самый старый отзыв модерации.
/// </summary>
/// <remarks>
/// <para>
/// Счётчики берутся из тех же предикатов, что фильтры разделов: <c>is_published = false</c>
/// (вкладка «На модерации»), <c>is_verified = false</c> (<c>AdminBarberStatus.Pending</c>,
/// без <c>is_active</c>), <c>is_active = false AND moderation_status = Active</c>
/// (<c>AdminCatalogueStatus.Hidden</c> для салонов), <c>moderation_status = Pending</c>
/// (<c>AdminCatalogueStatus.Pending</c> для салонов), два предиката очередей фотографий:
/// аватар — <c>avatar_moderation_status = Pending</c> при непустых <c>avatar_url</c> и
/// <c>avatar_submitted_at</c>, портфолио — <c>moderation_status = Pending</c>,
/// <c>is_email_verified = false</c> (<c>AdminUserStatus.Unverified</c> при <c>role=all</c>),
/// <c>barber_requests.status = Pending</c> и <c>bookings.status = Confirmed</c>. Поэтому
/// число в сводке — это ровно размер очереди раздела.
/// </para>
/// <para>
/// «Самый старый» — не отдельный SQL: обработчик переиспользует существующее чтение
/// <c>GetAdminModerationPageAsync(cursor: null, limit: 1)</c> с тем же фильтром,
/// теми же join'ами и тем же FIFO-порядком <c>updated_at ASC, id ASC</c>. Своё <c>ORDER BY</c>
/// рядом с очередью рано или поздно разошлось бы с ней. Ждать отзыв начинает с момента
/// попадания в очередь, а не создания: правка опубликованного отзыва возвращает его сюда
/// уже с новым временем ожидания.
/// </para>
/// <para>
/// Вызовы последовательные: <c>DbContext</c> не потокобезопасен, поэтому <c>Task.WhenAll</c>
/// здесь запрещён. Счётчик и страница — два чтения без транзакции; если отзыв опубликовали
/// между ними, <c>PendingCount &gt; 0</c> при <c>Oldest = null</c>. Это гонка двух чтений,
/// а не ошибка: транзакция ради двух <c>SELECT</c> не нужна, а экран покажет только число.
/// </para>
/// <para>
/// Никаких решений об авторизации: доступ проверяет контроллер. PII в ответ не добавляется —
/// из очереди берутся только имена, email клиента остаётся в <c>GET api/admin/reviews</c>,
/// а новые счётчики — только числа.
/// </para>
/// </remarks>
public sealed class GetAdminSummaryHandler
    : IQueryHandler<AdminSummaryResponse, GetAdminSummaryQuery>
{
    private readonly IReviewRepository _reviews;
    private readonly IBarberProfileRepository _barbers;
    private readonly ISalonRepository _salons;
    private readonly IUserRepository _users;
    private readonly IBarberRequestRepository _barberRequests;
    private readonly IBookingRepository _bookings;

    public GetAdminSummaryHandler(
        IReviewRepository reviews,
        IBarberProfileRepository barbers,
        ISalonRepository salons,
        IUserRepository users,
        IBarberRequestRepository barberRequests,
        IBookingRepository bookings)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        ArgumentNullException.ThrowIfNull(barbers);
        ArgumentNullException.ThrowIfNull(salons);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(barberRequests);
        ArgumentNullException.ThrowIfNull(bookings);

        _reviews = reviews;
        _barbers = barbers;
        _salons = salons;
        _users = users;
        _barberRequests = barberRequests;
        _bookings = bookings;
    }

    /// <summary>
    /// Возвращает число отзывов на модерации с самым старым из них, число неподтверждённых
    /// барберов, два счётчика салонов, две очереди фотографий, число пользователей без
    /// подтверждения email, очередь заявок и число подтверждённых броней.
    /// </summary>
    public async Task<Result<AdminSummaryResponse, Failure>> Handle(
        GetAdminSummaryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var pendingResult = await _reviews.CountPendingAsync(cancellationToken);
        if (pendingResult.IsFailure)
        {
            return pendingResult.Error;
        }

        // Первая строка существующей FIFO-страницы очереди, а не второй запрос «самого старого».
        var pageResult = await _reviews.GetAdminModerationPageAsync(
            cursor: null,
            limit: 1,
            cancellationToken);
        if (pageResult.IsFailure)
        {
            return pageResult.Error;
        }

        var barbersResult = await _barbers.CountPendingAsync(cancellationToken);
        if (barbersResult.IsFailure)
        {
            return barbersResult.Error;
        }

        var salonsResult = await _salons.CountInactiveAsync(cancellationToken);
        if (salonsResult.IsFailure)
        {
            return salonsResult.Error;
        }

        // Второй счётчик салонов — очередь модерации, а не «выключенные»: у него другой
        // предикат (moderation_status = Pending) и другое поле в ответе.
        var pendingModerationResult = await _salons.CountPendingModerationAsync(cancellationToken);
        if (pendingModerationResult.IsFailure)
        {
            return pendingModerationResult.Error;
        }

        // Две очереди фотографий: те же предикаты и тот же порядок, что у чтений
        // GetAdminAvatarPhotos / GetAdminPortfolioPhotos. Последовательно, а не WhenAll:
        // чтения идут в один DbContext.
        var avatarsResult = await _users.CountPendingAvatarsAsync(cancellationToken);
        if (avatarsResult.IsFailure)
        {
            return avatarsResult.Error;
        }

        var portfolioResult = await _barbers.CountPendingPortfolioPhotosAsync(cancellationToken);
        if (portfolioResult.IsFailure)
        {
            return portfolioResult.Error;
        }

        // Остальные счётчики разделов: пользователи без подтверждения email, очередь заявок
        // и подтверждённые брони. Тоже последовательно — тот же DbContext.
        var unverifiedUsersResult = await _users.CountUnverifiedAsync(cancellationToken);
        if (unverifiedUsersResult.IsFailure)
        {
            return unverifiedUsersResult.Error;
        }

        var pendingRequestsResult = await _barberRequests.CountPendingAsync(cancellationToken);
        if (pendingRequestsResult.IsFailure)
        {
            return pendingRequestsResult.Error;
        }

        var confirmedBookingsResult = await _bookings.CountConfirmedAsync(cancellationToken);
        if (confirmedBookingsResult.IsFailure)
        {
            return confirmedBookingsResult.Error;
        }

        var oldest = pageResult.Value.Count > 0
            ? ToOldest(pageResult.Value[0])
            : null;

        return new AdminSummaryResponse(
            new AdminSummaryReviewsResponse(pendingResult.Value, oldest),
            new AdminSummaryBarbersResponse(barbersResult.Value),
            new AdminSummarySalonsResponse(salonsResult.Value, pendingModerationResult.Value),
            new AdminSummaryPhotosResponse(avatarsResult.Value, portfolioResult.Value),
            new AdminSummaryUsersResponse(unverifiedUsersResult.Value),
            new AdminSummaryBarberRequestsResponse(pendingRequestsResult.Value),
            new AdminSummaryBookingsResponse(confirmedBookingsResult.Value));
    }

    /// <summary>
    /// Компактная строка очереди: только то, что показывает блок сводки.
    /// </summary>
    private static AdminSummaryOldestReviewResponse ToOldest(AdminReviewResponse row)
        => new(
            row.Id,
            row.Rating,
            row.UpdatedAt,
            row.Barber.DisplayName,
            row.Customer.FullName);
}
