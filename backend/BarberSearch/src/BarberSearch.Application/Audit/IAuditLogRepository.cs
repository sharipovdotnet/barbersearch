using BarberSearch.Application.Audit.Admin;
using BarberSearch.Contracts.Audit.Admin;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Audit;

/// <summary>
/// Чтение журнала изменений для администратора.
/// </summary>
/// <remarks>
/// Запись в журнал — не операция этого интерфейса: строки пишет <c>AuditTrail</c> при
/// сохранении изменений, а чтение только возвращает уже записанное. Слой Application
/// не знает инфраструктурный <c>AuditLogEntry</c>: наружу отдаётся контракт
/// <see cref="AdminAuditEntryResponse"/>, разбор <c>payload</c> — забота реализации.
/// </remarks>
public interface IAuditLogRepository
{
    /// <summary>
    /// Возвращает страницу журнала, свежие записи сверху.
    /// </summary>
    /// <param name="filters">Разобранные фильтры типа, автора и периода.</param>
    /// <param name="cursor">Курсор продолжения; <c>null</c> — первая страница.</param>
    /// <param name="limit">Размер страницы; не положительный — <c>admin.audit.page.limit</c>.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<AdminAuditEntryResponse>, Failure>> GetAdminPageAsync(
        AdminAuditFilters filters,
        AdminAuditCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default);
}
