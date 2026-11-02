namespace BarberSearch.Contracts.Bookings.Admin;

/// <summary>
/// Барбер админской брони: профиль и отображаемое имя.
/// </summary>
/// <remarks>
/// Идентификатор профиля нужен, чтобы поддержка нашла барбера в админке и передала ему
/// контекст визита; отображаемое имя — единственное, что понятно человеку в тикете.
/// </remarks>
/// <param name="ProfileId">Идентификатор профиля барбера.</param>
/// <param name="DisplayName">Отображаемое имя барбера.</param>
public record AdminBookingBarberResponse(Guid ProfileId, string DisplayName);
