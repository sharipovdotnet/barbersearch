namespace BarberSearch.Application.Push;

/// <summary>
/// Снимок салона, мастера, услуги и пояса для текста push-уведомления.
/// </summary>
/// <remarks>
/// Читается один раз при создании брони: событие создания push не должно тянуть каталог
/// повторно при доставке, а переименование услуги или салона не меняет уже отправленный
/// текст. Любая отсутствующая часть — <c>null</c>: внутренние join'ы источника не находят
/// историческую строку, и текст деградирует, а не падает.
/// </remarks>
/// <param name="SalonName">Название салона или <c>null</c>.</param>
/// <param name="BarberName">Имя мастера или <c>null</c>.</param>
/// <param name="ServiceName">Название услуги или <c>null</c>.</param>
/// <param name="SalonTimeZoneId">Пояс салона (IANA) или <c>null</c>.</param>
public sealed record PushBookingSnapshot(
    string? SalonName,
    string? BarberName,
    string? ServiceName,
    string? SalonTimeZoneId);
