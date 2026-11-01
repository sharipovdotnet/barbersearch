using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.HairServices.Admin;

namespace BarberSearch.Application.HairServices.Admin.GetAdminHairServices;

/// <summary>
/// Запрос админского справочника услуг: фильтр статуса и поиск по названию.
/// </summary>
/// <remarks>
/// <paramref name="Status"/> принимает <c>all</c> (по умолчанию), <c>active</c> или
/// <c>hidden</c>. Неизвестное значение отклоняет обработчик, а не model binding: клиенту
/// нужен публичный код ошибки. Страницы нет — словарь отдаётся целиком.
/// </remarks>
/// <param name="Status">Фильтр: <c>all</c>, <c>active</c> или <c>hidden</c>.</param>
/// <param name="Search">Поиск по названию; пустая строка — без фильтра.</param>
public record GetAdminHairServicesQuery(string? Status, string? Search)
    : IQuery<IReadOnlyList<AdminHairServiceResponse>>;
