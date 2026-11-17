using BarberSearch.Domain.Bookings;

namespace BarberSearch.Application.Bookings.BookingChangeEmail;

/// <summary>
/// Ставит в очередь письмо клиенту об отмене или переносе записи, сделанных в приложении.
/// </summary>
/// <remarks>
/// <para>
/// Письмо — замена push'у там, где приложение молчит: клиент отменил или перенёс запись сам
/// в приложении, push не создаётся (экран уже подтвердил действие), а история отмен в шторке
/// не хранится. Почта хранит (AGENT.md §20.11).
/// </para>
/// <para>
/// Гейт тот же, что у push, но с обратным знаком: письмо уходит только при
/// <c>isClientOwner &amp;&amp; IClientSurface.IsMobileApp</c>. Действие сотрудника салона и
/// действие клиента с веба письма не создают — там работает push (либо экран уже всё показал).
/// </para>
/// <para>
/// Постановка best-effort: строка пишется <b>после</b> сохранения перехода, в том же scope
/// запроса, а любой сбой логируется предупреждением без персональных данных и не меняет
/// результат команды. В транзакцию перехода письмо не вставляется: конфликт по ключу
/// дедупликации сделал бы транзакцию недействительной и откатил бы уже сохранённую отмену.
/// </para>
/// </remarks>
public interface IBookingChangeEmailNotifier
{
    /// <summary>
    /// Ставит письмо об отмене записи, если действие сделал клиент в мобильном приложении.
    /// </summary>
    /// <param name="booking">Отменённая бронь после сохранения перехода.</param>
    /// <param name="isClientOwner">Действие совершил клиент-владелец брони.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task NotifyCancelledAsync(
        Booking booking,
        bool isClientOwner,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ставит письмо о переносе визита, если действие сделал клиент в мобильном приложении.
    /// </summary>
    /// <param name="booking">Перенесённая бронь после сохранения перехода.</param>
    /// <param name="isClientOwner">Действие совершил клиент-владелец брони.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task NotifyRescheduledAsync(
        Booking booking,
        bool isClientOwner,
        CancellationToken cancellationToken = default);
}
