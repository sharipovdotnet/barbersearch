using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Admin;

namespace BarberSearch.Application.Admin.GetAdminSummary;

/// <summary>
/// Запрос админской сводки «что требует внимания».
/// </summary>
/// <remarks>
/// Пустая запись: у эндпоинта <c>GET api/admin/summary</c> нет ни параметров, ни пагинации,
/// ни курсора. Доступ проверяет контроллер политикой <c>AdminOnly</c>, поэтому решений
/// об авторизации в обработчике нет.
/// </remarks>
public sealed record GetAdminSummaryQuery : IQuery<AdminSummaryResponse>;
