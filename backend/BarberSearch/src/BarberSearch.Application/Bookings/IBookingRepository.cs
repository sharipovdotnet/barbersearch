using BarberSearch.Application.Common;
using BarberSearch.Contracts.Bookings.Barber;
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
    /// Страница броней выбранного скоупа.
    /// </summary>
    /// <remarks>
    /// <paramref name="userId"/> = <c>null</c> — все брони (сценарий администратора).
    /// Граница и направление keyset-сравнения зависят от <paramref name="scope"/>:
    /// <c>all</c> — прежний <c>start_at DESC, id ASC</c>, «ближайшие» — активные брони
    /// с <c>EndAt &gt; now</c> по возрастанию, история — всё остальное по убыванию.
    /// Keyset-условие берётся из <paramref name="cursor"/>; <paramref name="limit"/> —
    /// это pageSize + 1, чтобы обработчик отличил последнюю страницу от неполной.
    /// Читается без отслеживания и проецируется сразу в <see cref="BookingResponse"/>.
    /// </remarks>
    /// <param name="userId">Клиент или <c>null</c> для всех броней.</param>
    /// <param name="scope">Скоуп выдачи.</param>
    /// <param name="now">Текущий момент UTC — граница скоупов.</param>
    /// <param name="cursor">Позиция продолжения или <c>null</c> для первой страницы.</param>
    /// <param name="limit">Сколько строк прочитать.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<BookingResponse>, Failure>> GetPageAsync(
        Guid? userId,
        BookingScope scope,
        DateTimeOffset now,
        BookingCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Страница записей барбера для кабинета: ближайшие или история.
    /// </summary>
    /// <remarks>
    /// Фильтр по <paramref name="barberProfileId"/> обязателен — профиль приходит из claim'а
    /// сессии. Граница скоупа и направление keyset-сравнения зависят от
    /// <paramref name="scope"/>: «ближайшие» — активные брони с <c>EndAt &gt; now</c> по
    /// возрастанию, «история» — всё остальное по убыванию. Проекция собирает имя и телефон
    /// клиента, услугу и салон; email и идентификаторы в ответ не попадают.
    /// </remarks>
    /// <param name="barberProfileId">Профиль барбера из claim'а текущей сессии.</param>
    /// <param name="scope">Скоуп выдачи.</param>
    /// <param name="now">Текущий момент UTC — граница скоупов.</param>
    /// <param name="cursor">Позиция продолжения или <c>null</c> для первой страницы.</param>
    /// <param name="limit">Сколько строк прочитать (pageSize + 1).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<BarberBookingResponse>, Failure>> GetBarberPageAsync(
        Guid barberProfileId,
        BarberBookingScope scope,
        DateTimeOffset now,
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
    /// Берёт транзакционную advisory-блокировку на барбера, сериализуя создание его броней.
    /// </summary>
    /// <remarks>
    /// Блокировка живёт до конца транзакции, поэтому вызывать метод можно только внутри
    /// <c>IUnitOfWork.ExecuteAsync</c>: вне транзакции PostgreSQL снимет её сразу после
    /// выполнения запроса. Транзакцию метод не открывает — её границей владеет вызывающий
    /// сценарий. Блокировка не решает, кто получит слот (это делает exclusion-ограничение
    /// <c>ex_bookings_no_overlap</c>), а не даёт параллельным GiST-проверкам уйти в deadlock.
    /// <para>
    /// Ожидание ограничено <c>lock_timeout</c> (2 секунды, задаётся через <c>SET LOCAL</c>
    /// в той же транзакции). Если блокировку взять не удалось, метод возвращает
    /// <c>Error.Conflict</c> с кодом <c>booking.add.lockTimeout</c> (409): это осознанно
    /// ограниченное ожидание, а не сбой сервера, поэтому клиенту достаточно повторить запрос.
    /// </para>
    /// </remarks>
    /// <param name="barberProfileId">Барбер, чьи брони сериализуются.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Успех после взятия блокировки; <c>Failure</c> — истёк <c>lock_timeout</c>.</returns>
    Task<UnitResult<Failure>> AcquireBarberLockAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет бронь. Повтор ключа идемпотентности — <c>Error.Conflict</c>.
    /// </summary>
    /// <remarks>
    /// Метод только добавляет сущность и сохраняет её: транзакцию не открывает, потому что
    /// её границей владеет <c>IUnitOfWork</c> (R-15). Неудачная вставка снимается
    /// с change tracker, чтобы повторный <c>SaveChanges</c> в том же scope не пытался
    /// записать её снова.
    /// </remarks>
    Task<UnitResult<Failure>> AddAsync(Booking booking, CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет изменения брони (подтверждение, отмена, завершение, неявка, заметка).
    /// </summary>
    Task<UnitResult<Failure>> UpdateAsync(Booking booking, CancellationToken cancellationToken = default);
}
