using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Salons.Admin;

namespace BarberSearch.Application.Salons.Admin.GetAdminSalons;

/// <summary>
/// Запрос админского справочника салонов: фильтр статуса и поиск по названию и городу.
/// </summary>
/// <remarks>
/// <paramref name="Status"/> принимает <c>all</c> (по умолчанию), <c>active</c> или
/// <c>hidden</c>; неизвестное значение отклоняет обработчик публичным кодом ошибки.
/// Страницы нет — словарь отдаётся целиком.
/// </remarks>
/// <param name="Status">Фильтр: <c>all</c>, <c>active</c> или <c>hidden</c>.</param>
/// <param name="Search">Поиск по названию или городу; пустая строка — без фильтра.</param>
public record GetAdminSalonsQuery(string? Status, string? Search)
    : IQuery<IReadOnlyList<AdminSalonResponse>>;
