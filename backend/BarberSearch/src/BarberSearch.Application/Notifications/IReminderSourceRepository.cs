using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Notifications;

/// <summary>
/// Источник данных для напоминаний: подтверждённые визиты барбера и пояс его основного салона.
/// </summary>
/// <remarks>
/// <para>
/// Отдельная абстракция, а не методы <c>IBookingRepository</c>: напоминанию нужна проекция
/// с названиями услуги и салона и связка «пользователь → профиль барбера → брони», которой
/// у брони нет. Один узкий порт держит все три запроса тика, поэтому реализация может
/// сделать их без обращения к агрегатам.
/// </para>
/// <para>
/// «Пользователь → брони» идёт через <c>barber_profiles.user_id</c>: уведомление принадлежит
/// пользователю-барберу (решение D5), а бронь хранит <c>barber_profile_id</c>.
/// </para>
/// </remarks>
public interface IReminderSourceRepository
{
    /// <summary>
    /// Пояс основного салона барбера (<c>barber_salons.is_primary = true</c> → <c>salons.time_zone_id</c>).
    /// </summary>
    /// <remarks>
    /// Возвращает <c>null</c>, если у барбера нет основного салона: дайджест такому барберу
    /// не создаётся — локальные сутки и время отправки считать не от чего.
    /// </remarks>
    /// <param name="barberUserId">Пользователь-барбер.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<string?, Failure>> GetPrimarySalonTimeZoneIdAsync(
        Guid barberUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Подтверждённые брони барбера, начинающиеся в интервале <c>(fromExclusive, toInclusive]</c>.
    /// </summary>
    /// <remarks>
    /// Возвращаются только <c>status = Confirmed</c> (решение D4): <c>Pending</c> ещё не
    /// подтверждена, напоминать о ней — шум. Реализация может дополнительно отбросить брони,
    /// для которых напоминание уже создано, но не обязана: дедупликацию закрывает
    /// <see cref="IBarberNotificationRepository.TryAddAsync"/>, а окно по
    /// <c>settings.lead_minutes</c> проверяет обработчик.
    /// </remarks>
    /// <param name="barberUserId">Пользователь-барбер.</param>
    /// <param name="fromExclusive">Нижняя граница (строго больше).</param>
    /// <param name="toInclusive">Верхняя граница (включительно).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<ReminderBooking>, Failure>> GetUpcomingBookingsAsync(
        Guid barberUserId,
        DateTimeOffset fromExclusive,
        DateTimeOffset toInclusive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Подтверждённые брони барбера в интервале <c>[fromInclusive, toExclusive)</c> по возрастанию
    /// начала — содержимое локальных суток салона для дайджеста.
    /// </summary>
    /// <remarks>
    /// Порядок обязателен: первая строка задаёт «первую запись дня» в дайджесте. Границы
    /// интервала обработчик считает в поясе основного салона и передаёт уже в UTC.
    /// </remarks>
    /// <param name="barberUserId">Пользователь-барбер.</param>
    /// <param name="fromInclusive">Начало локальных суток в UTC (включительно).</param>
    /// <param name="toExclusive">Начало следующих локальных суток в UTC (не включается).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<ReminderBooking>, Failure>> GetDayBookingsAsync(
        Guid barberUserId,
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken = default);
}
