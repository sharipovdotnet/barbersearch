using BarberSearch.Domain.Bookings;

namespace BarberSearch.Contracts.Bookings.Admin;

/// <summary>
/// Бронь в админском списке и карточке поддержки: визит вместе с клиентом, барбером,
/// услугой и салоном.
/// </summary>
/// <remarks>
/// <para>
/// Отдельный контракт от <c>BookingResponse</c> (клиент) и <c>BarberBookingResponse</c>
/// (кабинет барбера): администратору нужен весь контекст визита, чтобы ответить клиенту
/// по телефону или письму. <see cref="Customer"/> несёт email и телефон — это PII, и она
/// допустима <b>только</b> в этом админском контракте: в публичные чтения броней email
/// не попадает.
/// </para>
/// <para>
/// <see cref="Status"/> сериализуется именем <see cref="BookingStatus"/> (<c>Pending</c>,
/// <c>Confirmed</c>, <c>Cancelled</c>, <c>Completed</c>, <c>NoShow</c>), как и везде в JSON.
/// <see cref="StartAt"/> и <see cref="EndAt"/> — UTC; показывать их нужно в
/// <c>Salon.TimeZoneId</c>, а не в поясе браузера.
/// </para>
/// <para>
/// <see cref="Price"/> — снимок цены, зафиксированный в брони при создании, а не текущая
/// цена услуги: после правки прайса история не должна меняться.
/// <see cref="BarberNote"/> — поле барбера; администратор его только читает и не редактирует.
/// </para>
/// </remarks>
/// <param name="Id">Идентификатор брони.</param>
/// <param name="Status">Текущий статус брони; в JSON — имя значения.</param>
/// <param name="StartAt">Начало визита в UTC.</param>
/// <param name="EndAt">Окончание визита в UTC.</param>
/// <param name="Price">Цена, зафиксированная в брони при создании.</param>
/// <param name="CustomerNote">Заметка клиента при создании брони; <c>null</c> — заметки нет.</param>
/// <param name="BarberNote">Заметка барбера; администратор её только читает.</param>
/// <param name="CancellationReason">Причина отмены, оставленная барбером; только чтение.</param>
/// <param name="CreatedAt">Когда бронь создана, в UTC.</param>
/// <param name="Customer">Клиент визита: имя, email и телефон.</param>
/// <param name="Barber">Барбер визита: профиль и отображаемое имя.</param>
/// <param name="Service">Услуга визита.</param>
/// <param name="Salon">Салон визита, включая часовой пояс для показа времени.</param>
public record AdminBookingResponse(
    Guid Id,
    BookingStatus Status,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    decimal Price,
    string? CustomerNote,
    string? BarberNote,
    string? CancellationReason,
    DateTimeOffset CreatedAt,
    AdminBookingCustomerResponse Customer,
    AdminBookingBarberResponse Barber,
    AdminBookingServiceResponse Service,
    AdminBookingSalonResponse Salon);
