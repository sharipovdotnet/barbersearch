using BarberSearch.Application.Common;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Domain.Bookings;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Bookings;

/// <summary>
/// Доступ к агрегату <see cref="Booking"/>: чтение и запись.
/// </summary>
/// <remarks>
/// Метода удаления нет: бронь не удаляют, а переводят в статус
/// <c>Cancelled</c> через <c>Booking.Cancel()</c>.
/// «Активной» здесь считается бронь в статусе <c>Pending</c> или <c>Confirmed</c> —
/// именно она занимает слот.
/// </remarks>
public interface IBookingRepository
{
    /// <summary>
    /// Бронь по идентификатору. Сущность отслеживается контекстом.
    /// </summary>
    Task<Result<Booking, Failure>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Бронь по ключу идемпотентности в рамках пользователя.
    /// Повторный запрос создания с тем же ключом должен вернуть существующую бронь,
    /// а не создать вторую (см. AGENT.md §15.5).
    /// </summary>
    Task<Result<Booking, Failure>> GetByIdempotencyKeyAsync(
        Guid userId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Страница броней клиента, свежие сверху. Читается без отслеживания.
    /// </summary>
    Task<Result<IReadOnlyList<Booking>, Failure>> GetByUserAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Страница броней в порядке <c>start_at DESC, id</c>.
    /// </summary>
    /// <remarks>
    /// <paramref name="userId"/> = <c>null</c> — все брони (сценарий администратора).
    /// Keyset-условие берётся из <paramref name="cursor"/>; <paramref name="limit"/> —
    /// это pageSize + 1, чтобы обработчик отличил последнюю страницу от неполной.
    /// Читается без отслеживания и проецируется сразу в <see cref="BookingResponse"/>.
    /// </remarks>
    /// <param name="userId">Клиент или <c>null</c> для всех броней.</param>
    /// <param name="cursor">Позиция продолжения или <c>null</c> для первой страницы.</param>
    /// <param name="limit">Сколько строк прочитать.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<BookingResponse>, Failure>> GetPageAsync(
        Guid? userId,
        BookingCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Активные брони барбера, пересекающиеся с интервалом, — вход для расчёта свободных слотов.
    /// Читается без отслеживания.
    /// </summary>
    Task<Result<IReadOnlyList<Booking>, Failure>> GetActiveByBarberAsync(
        Guid barberProfileId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Занят ли интервал у барбера. Проверка перед вставкой; окончательная защита от
    /// двойной брони — exclusion-ограничение в БД.
    /// </summary>
    Task<Result<bool, Failure>> HasActiveOverlapAsync(
        Guid barberProfileId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет бронь. Повтор ключа идемпотентности — <c>Error.Conflict</c>.
    /// </summary>
    Task<UnitResult<Failure>> AddAsync(Booking booking, CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет изменения брони (подтверждение, отмена, завершение, неявка, заметка).
    /// </summary>
    Task<UnitResult<Failure>> UpdateAsync(Booking booking, CancellationToken cancellationToken = default);
}
