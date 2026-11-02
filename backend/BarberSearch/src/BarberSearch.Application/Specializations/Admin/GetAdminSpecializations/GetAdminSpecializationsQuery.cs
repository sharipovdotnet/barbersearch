using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Specializations.Admin;

namespace BarberSearch.Application.Specializations.Admin.GetAdminSpecializations;

/// <summary>
/// Запрос админского справочника специализаций: фильтр статуса и поиск по названию.
/// </summary>
/// <remarks>
/// <paramref name="Status"/> принимает <c>all</c> (по умолчанию), <c>active</c> или
/// <c>hidden</c>; неизвестное значение отклоняет обработчик публичным кодом ошибки.
/// Страницы нет — словарь отдаётся целиком.
/// </remarks>
/// <param name="Status">Фильтр: <c>all</c>, <c>active</c> или <c>hidden</c>.</param>
/// <param name="Search">Поиск по названию; пустая строка — без фильтра.</param>
public record GetAdminSpecializationsQuery(string? Status, string? Search)
    : IQuery<IReadOnlyList<AdminSpecializationResponse>>;
