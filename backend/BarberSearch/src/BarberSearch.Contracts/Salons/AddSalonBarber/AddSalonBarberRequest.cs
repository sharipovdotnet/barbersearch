namespace BarberSearch.Contracts.Salons.AddSalonBarber;

/// <summary>
/// Запрос на прикрепление барбера к салону.
/// </summary>
/// <remarks>
/// Прикрепляет именно профиль барбера: салон берётся из маршрута, поэтому подменить
/// его телом нельзя. Профиль должен существовать; повторная привязка — <c>409</c>.
/// </remarks>
/// <param name="BarberProfileId">Идентификатор профиля барбера.</param>
public record AddSalonBarberRequest(Guid BarberProfileId);
