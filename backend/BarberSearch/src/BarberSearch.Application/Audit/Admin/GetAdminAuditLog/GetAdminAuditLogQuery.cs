using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Audit.Admin;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Application.Audit.Admin.GetAdminAuditLog;

/// <summary>
/// Запрос админского журнала изменений.
/// </summary>
/// <remarks>
/// Даты приходят строками, а не <c>DateOnly?</c>: кривую дату должно отвергнуть не
/// model binding с общим <c>ValidationProblemDetails</c>, а обработчик — с публичным кодом
/// <c>admin.audit.period</c>, на который клиент может опираться.
/// </remarks>
/// <param name="EntityType">Тип сущности: <c>all</c> (по умолчанию), <c>user</c>, <c>barber_profile</c> или <c>review</c>.</param>
/// <param name="Actor">Подстрока email автора; пустая — без фильтра.</param>
/// <param name="From">Начальная дата UTC-суток в формате <c>yyyy-MM-dd</c> включительно.</param>
/// <param name="To">Последняя дата UTC-суток в формате <c>yyyy-MM-dd</c> включительно.</param>
/// <param name="Cursor">Непрозрачный курсор следующей страницы.</param>
/// <param name="PageSize">Размер страницы; по умолчанию 20, максимум 100.</param>
public record GetAdminAuditLogQuery(
    string? EntityType,
    string? Actor,
    string? From,
    string? To,
    string? Cursor,
    int? PageSize) : IQuery<PagedResponse<AdminAuditEntryResponse>>;
