using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Bookings.CreateBooking;

namespace BarberSearch.Application.Bookings.CreateBooking;

/// <summary>
/// Команда создания брони.
/// </summary>
/// <remarks>
/// Состав полей описан контрактом <see cref="CreateBookingRequest"/> и здесь не дублируется:
/// команда только помечает операцию, данные приходят одним объектом — так же, как
/// <c>CreateBarberProfileCommand</c> с <c>BarberProfileCreateRequest</c>. Ключ идемпотентности
/// обязателен: при обрыве сети мобильный клиент повторяет запрос, и повтор с тем же ключом
/// должен вернуть уже созданную бронь, а не создать вторую
/// (см. <c>IBookingRepository.GetByIdempotencyKeyAsync</c> и AGENT.md §15.5).
/// <para>
/// <see cref="CustomerId"/> заполняет только сценарий «барбер записывает клиента»: при
/// <c>null</c> клиент берётся из токена (публичная запись клиента), при значении —
/// заказчик проверяется как активный пользователь с ролью <c>Customer</c>.
/// <see cref="ConfirmImmediately"/> переводит бронь в <c>Confirmed</c> сразу: барбер
/// не подтверждает запись, которую внёс сам.
/// </para>
/// <para>
/// Гостевые поля (<see cref="GuestFirstName"/>, <see cref="GuestLastName"/>,
/// <see cref="GuestPhone"/>) заполняет только ручной путь барбера
/// (<see cref="ConfirmImmediately"/> = <c>true</c> и <see cref="CustomerId"/> = <c>null</c>):
/// запись создаётся по имени и телефону, аккаунт не создаётся. Публичный путь клиента гостевые
/// поля отклоняет: клиент там всегда берётся из токена. Заполненные одновременно
/// <see cref="CustomerId"/> и гостевые поля — <c>400 booking.create.customerAmbiguous</c>.
/// </para>
/// </remarks>
public record CreateBookingCommand(
    CreateBookingRequest Request,
    Guid? CustomerId = null,
    bool ConfirmImmediately = false,
    string? GuestFirstName = null,
    string? GuestLastName = null,
    string? GuestPhone = null) : ICommand;
