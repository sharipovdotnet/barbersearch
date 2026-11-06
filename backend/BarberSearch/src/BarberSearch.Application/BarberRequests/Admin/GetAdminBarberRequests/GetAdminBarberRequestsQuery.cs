using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberRequests.Admin;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Application.BarberRequests.Admin.GetAdminBarberRequests;

/// <summary>
/// Запрос администраторского списка заявок в барберы.
/// </summary>
/// <remarks>
/// <paramref name="Status"/> принимает <c>pending</c> (по умолчанию) или <c>all</c>.
/// Неизвестное значение отклоняет обработчик, а не model binding: клиенту нужен публичный
/// код ошибки, а не общий <c>ValidationProblemDetails</c> от биндера.
/// </remarks>
/// <param name="Status">Фильтр: <c>pending</c> или <c>all</c>.</param>
/// <param name="Cursor">Курсор следующей страницы.</param>
/// <param name="PageSize">Размер страницы; <c>null</c> — значение по умолчанию.</param>
public record GetAdminBarberRequestsQuery(
    string? Status,
    string? Cursor,
    int? PageSize) : IQuery<PagedResponse<AdminBarberRequestResponse>>;
