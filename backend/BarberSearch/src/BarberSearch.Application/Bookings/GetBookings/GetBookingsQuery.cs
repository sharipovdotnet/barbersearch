using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Application.Bookings.GetBookings;

/// <summary>
/// Запрос списка броней с keyset-пагинацией.
/// </summary>
/// <remarks>
/// Обычный пользователь всегда видит только свои брони. Администратор видит все;
/// <paramref name="UserId"/> позволяет ему сузить выдачу до одного клиента.
/// Без <paramref name="UserId"/> не-администратор получает собственный список.
/// <para>
/// <paramref name="Scope"/> приходит строкой: разбор и ошибка живут в обработчике, как у
/// списка записей барбера, поэтому контроллер только пробрасывает параметр. Пустая строка
/// и <c>null</c> означают <c>all</c> — прежнее поведение без скоупа.
/// </para>
/// </remarks>
/// <param name="UserId">Фильтр по клиенту; <c>null</c> — все брони для admin или свои для остальных.</param>
/// <param name="Scope">Скоуп выдачи: <c>all</c>, <c>upcoming</c> или <c>past</c>.</param>
/// <param name="Cursor">Курсор следующей страницы.</param>
/// <param name="PageSize">Размер страницы; <c>null</c> — значение по умолчанию.</param>
public record GetBookingsQuery(
    Guid? UserId,
    string? Scope,
    string? Cursor,
    int? PageSize) : IQuery<PagedResponse<BookingResponse>>;
