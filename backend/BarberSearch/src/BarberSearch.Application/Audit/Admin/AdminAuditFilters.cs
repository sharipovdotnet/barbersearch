namespace BarberSearch.Application.Audit.Admin;

/// <summary>
/// Разобранные фильтры журнала изменений.
/// </summary>
/// <remarks>
/// Обработчик переводит строки query string в этот тип: репозиторий не разбирает даты
/// и не решает, что считать пустым фильтром. Период уже переведён в UTC-сутки:
/// <see cref="OccurredFrom"/> включительно, <see cref="OccurredToExclusive"/> не включительно.
/// </remarks>
/// <param name="EntityType">Тип сущности; <see cref="AdminAuditEntityType.All"/> — без фильтра.</param>
/// <param name="Actor">Подстрока email автора; <c>null</c> — без фильтра.</param>
/// <param name="OccurredFrom">Начало периода включительно (00:00 UTC); <c>null</c> — без нижней границы.</param>
/// <param name="OccurredToExclusive">Начало суток после последней даты (00:00 UTC следующего дня); <c>null</c> — без верхней границы.</param>
public record AdminAuditFilters(
    AdminAuditEntityType EntityType,
    string? Actor,
    DateTimeOffset? OccurredFrom,
    DateTimeOffset? OccurredToExclusive);
