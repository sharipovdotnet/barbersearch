namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Админская сводка «что требует внимания»: очереди четырёх разделов в одном ответе.
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
/// (<c>AdminCatalogueStatus.Hidden</c> для салонов) и два предиката очередей фотографий
/// (<c>avatar_moderation_status = Pending</c>, <c>moderation_status = Pending</c>). Иначе
/// число разошлось бы с разделом.
/// </para>
/// <para>
/// Email и телефон клиента в сводку не входят: только имена. Мутаций у сводки нет —
/// действия остаются в разделах. Порядок и глубина очередей — за
/// <c>GET api/admin/reviews</c>, <c>GET api/admin/barbers</c>, <c>GET api/admin/photos/…</c>
/// и админским каталогом.
/// </para>
/// </remarks>
/// <param name="Reviews">Очередь модерации: число и самый старый отзыв.</param>
/// <param name="Barbers">Барберы без подтверждения.</param>
/// <param name="Salons">Салоны, выключенные из поиска.</param>
/// <param name="Photos">Фотографии на проверке: аватары и портфолио.</param>
public record AdminSummaryResponse(
    AdminSummaryReviewsResponse Reviews,
    AdminSummaryBarbersResponse Barbers,
    AdminSummarySalonsResponse Salons,
    AdminSummaryPhotosResponse Photos);
