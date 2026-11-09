using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Reviews;
using BarberSearch.Application.Salons;
using BarberSearch.Contracts.Admin;
using BarberSearch.Contracts.Reviews.Admin;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Admin.GetAdminSummary;

/// <summary>
/// Обработчик админской сводки: три счётчика очередей и самый старый отзыв модерации.
/// </summary>
/// <remarks>
/// <para>
/// Счётчики берутся из тех же предикатов, что фильтры разделов: <c>is_published = false</c>
/// (вкладка «На модерации»), <c>is_verified = false</c> (<c>AdminBarberStatus.Pending</c>,
/// без <c>is_active</c>) и <c>is_active = false</c> (<c>AdminCatalogueStatus.Hidden</c>
/// для салонов). Поэтому число в сводке — это ровно размер очереди раздела.
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
/// из очереди берутся только имена, email клиента остаётся в <c>GET api/admin/reviews</c>.
/// </para>
/// </remarks>
public sealed class GetAdminSummaryHandler
    : IQueryHandler<AdminSummaryResponse, GetAdminSummaryQuery>
{
    private readonly IReviewRepository _reviews;
    private readonly IBarberProfileRepository _barbers;
    private readonly ISalonRepository _salons;

    public GetAdminSummaryHandler(
        IReviewRepository reviews,
        IBarberProfileRepository barbers,
        ISalonRepository salons)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        ArgumentNullException.ThrowIfNull(barbers);
        ArgumentNullException.ThrowIfNull(salons);

        _reviews = reviews;
        _barbers = barbers;
        _salons = salons;
    }

    /// <summary>
    /// Возвращает число отзывов на модерации с самым старым из них, число неподтверждённых
    /// барберов и число выключенных салонов.
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

        var oldest = pageResult.Value.Count > 0
            ? ToOldest(pageResult.Value[0])
            : null;

        return new AdminSummaryResponse(
            new AdminSummaryReviewsResponse(pendingResult.Value, oldest),
            new AdminSummaryBarbersResponse(barbersResult.Value),
            new AdminSummarySalonsResponse(salonsResult.Value));
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
