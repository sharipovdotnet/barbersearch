namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Админская сводка «что требует внимания»: счётчики очередей разделов в одном ответе.
/// </summary>
/// <remarks>
/// <para>
/// Это единственное агрегатное чтение админки. Оно не меняет существующие списки:
/// общее число нужно одному экрану, поэтому <c>total</c> в <see cref="Common.PagedResponse{T}"/>
/// не появляется.
/// </para>
/// <para>
/// Счётчики используют ровно те же предикаты, что фильтры разделов:
/// <c>is_published = false</c> (вкладка «На модерации»), <c>is_verified = false</c>
/// (<c>AdminBarberStatus.Pending</c>, без условия <c>is_active</c>), <c>is_active = false</c>
/// (<c>AdminCatalogueStatus.Hidden</c> для салонов) и <c>moderation_status = Pending</c>
/// (<c>AdminCatalogueStatus.Pending</c> для салонов), два предиката очередей фотографий
/// (<c>avatar_moderation_status = Pending</c>, <c>moderation_status = Pending</c>),
/// <c>is_email_verified = false</c> (<c>AdminUserStatus.Unverified</c>),
/// <c>barber_requests.status = Pending</c> и <c>bookings.status = Confirmed</c>. Иначе
/// число разошлось бы с разделом.
/// </para>
/// <para>
/// Email и телефон клиента в сводку не входят: только имена. Мутаций у сводки нет —
/// действия остаются в разделах. Порядок и глубина очередей — за существующими
/// админскими чтениями разделов.
/// </para>
/// </remarks>
/// <param name="Reviews">Очередь модерации: число и самый старый отзыв.</param>
/// <param name="Barbers">Барберы без подтверждения.</param>
/// <param name="Salons">Салоны, выключенные из поиска, и салоны на проверке.</param>
/// <param name="Photos">Фотографии на проверке: аватары и портфолио.</param>
/// <param name="Users">Пользователи без подтверждения email.</param>
/// <param name="BarberRequests">Заявки на роль барбера в очереди.</param>
/// <param name="Bookings">Брони в статусе «Подтверждена».</param>
public record AdminSummaryResponse(
    AdminSummaryReviewsResponse Reviews,
    AdminSummaryBarbersResponse Barbers,
    AdminSummarySalonsResponse Salons,
    AdminSummaryPhotosResponse Photos,
    AdminSummaryUsersResponse Users,
    AdminSummaryBarberRequestsResponse BarberRequests,
    AdminSummaryBookingsResponse Bookings);
