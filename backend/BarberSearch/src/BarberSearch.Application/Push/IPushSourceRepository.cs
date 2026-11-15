using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Push;

/// <summary>
/// Источник визитов для создания push-напоминаний клиентам.
/// </summary>
/// <remarks>
/// <para>
/// Отдельный узкий порт, а не <c>IBookingRepository</c>: напоминанию нужна проекция с именами
/// мастера, салона и услуги, которой у брони нет (она хранит только идентификаторы).
/// </para>
/// <para>
/// Возвращаются только <c>status = Confirmed</c> и только брони зарегистрированных клиентов
/// (<c>user_id IS NOT NULL</c>): гостю push отправить некуда. Окно <c>(now, now + lead]</c>
/// и правило «бронь создана не позже <c>start_at − lead</c>» — тоже обязанность запроса,
/// чтобы тик не тянул всю таблицу; обработчик повторяет окно в коде как последнюю защиту.
/// </para>
/// </remarks>
public interface IPushSourceRepository
{
    /// <summary>
    /// Подтверждённые визиты зарегистрированных клиентов, начинающиеся в интервале
    /// <c>(fromExclusive, toInclusive]</c> и созданные не позже <c>start_at − leadMinutes</c>.
    /// </summary>
    /// <param name="fromExclusive">Нижняя граница окна (строго больше) — текущий момент.</param>
    /// <param name="toInclusive">Верхняя граница окна (включительно) — <c>now + lead</c>.</param>
    /// <param name="leadMinutes">Окно напоминания в минутах: им же ограничивается возраст брони.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<PushReminderBooking>, Failure>> GetDueBookingsAsync(
        DateTimeOffset fromExclusive,
        DateTimeOffset toInclusive,
        int leadMinutes,
        CancellationToken cancellationToken = default);
}
