using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Salons.Admin;

namespace BarberSearch.Application.Salons.Admin.GetAdminSalons;

/// <summary>
/// Запрос админского справочника салонов: фильтр статуса и поиск по названию и городу.
/// </summary>
/// <remarks>
/// <paramref name="Status"/> принимает <c>all</c> (по умолчанию), <c>active</c>,
/// <c>hidden</c> или <c>pending</c>; неизвестное значение отклоняет обработчик публичным
/// кодом ошибки. <c>pending</c> — очередь модерации: он есть только у салонов, в отличие от
/// услуг и специализаций. Страницы нет — словарь отдаётся целиком.
/// </remarks>
/// <param name="Status">Фильтр: <c>all</c>, <c>active</c>, <c>hidden</c> или <c>pending</c>.</param>
/// <param name="Search">Поиск по названию или городу; пустая строка — без фильтра.</param>
public record GetAdminSalonsQuery(string? Status, string? Search)
    : IQuery<IReadOnlyList<AdminSalonResponse>>;
