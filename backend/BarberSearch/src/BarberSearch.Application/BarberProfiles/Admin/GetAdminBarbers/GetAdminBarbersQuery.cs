using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Application.BarberProfiles.Admin.GetAdminBarbers;

/// <summary>
/// Запрос админского списка барберов с поиском и фильтром по статусу.
/// </summary>
/// <remarks>
/// <paramref name="Status"/> принимает <c>all</c> (по умолчанию), <c>pending</c> или
/// <c>hidden</c>. Неизвестное значение отклоняет обработчик, а не model binding: клиенту нужен
/// публичный код ошибки, а не общий <c>ValidationProblemDetails</c> от биндера.
/// <paramref name="Search"/> ищет по имени и email; пустая строка — без фильтра.
/// </remarks>
/// <param name="Status">Фильтр: <c>all</c>, <c>pending</c> или <c>hidden</c>.</param>
/// <param name="Search">Поиск по имени или email.</param>
/// <param name="Cursor">Курсор следующей страницы.</param>
/// <param name="PageSize">Размер страницы; <c>null</c> — значение по умолчанию.</param>
public record GetAdminBarbersQuery(
    string? Status,
    string? Search,
    string? Cursor,
    int? PageSize) : IQuery<PagedResponse<AdminBarberResponse>>;
