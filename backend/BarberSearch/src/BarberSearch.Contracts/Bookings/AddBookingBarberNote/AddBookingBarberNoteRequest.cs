namespace BarberSearch.Contracts.Bookings.AddBookingBarberNote;

/// <summary>
/// Данные запроса на добавление заметки барбера к брони.
/// </summary>
/// <remarks>
/// Заметка — часть брони, поэтому отдельного ресурса у неё нет: она пишется в саму бронь,
/// а идентификатор брони приходит из маршрута.
/// </remarks>
public record AddBookingBarberNoteRequest(string Note);
