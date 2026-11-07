using BarberSearch.Application.Notifications;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Журнал идемпотентности вебхука Telegram.
/// </summary>
/// <remarks>
/// <para>
/// Обработка обновления начинается с <c>INSERT ... ON CONFLICT (update_id) DO NOTHING</c>:
/// если строка уже есть, обновление обработано этим или другим экземпляром, и повторять
/// действие нельзя. Так Telegram может ретраить запрос сколько угодно — ответ всё равно
/// <c>200</c>, а привязка чата не выполнится дважды.
/// </para>
/// <para>
/// Таблица инфраструктурная (<c>telegram_updates</c>), поэтому репозиторий не наследует
/// <c>RepositoryBase</c>: у строки нет доменных правил и аудита.
/// </para>
/// </remarks>
public sealed class TelegramUpdateRepository : ITelegramUpdateRepository
{
    private readonly ApplicationDbContext _context;

    public TelegramUpdateRepository(ApplicationDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public async Task<Result<bool, Failure>> TryRegisterAsync(
        long updateId,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken = default)
    {
        var inserted = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO telegram_updates (update_id, received_at)
             VALUES ({updateId}, {receivedAt})
             ON CONFLICT (update_id) DO NOTHING
             """,
            cancellationToken);

        return inserted > 0;
    }
}
