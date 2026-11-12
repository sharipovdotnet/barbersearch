namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Подтверждённые брони в админской сводке.
/// </summary>
/// <remarks>
/// <see cref="ConfirmedCount"/> — ровно фильтр «Подтверждена» админского списка броней:
/// <c>status = Confirmed</c>, включая уже прошедшие визиты. Это не «предстоящие» брони:
/// условие <c>start_at &gt;= now</c> разошлось бы с размером раздела.
/// </remarks>
/// <param name="ConfirmedCount">Сколько броней подтверждено.</param>
public record AdminSummaryBookingsResponse(int ConfirmedCount);
