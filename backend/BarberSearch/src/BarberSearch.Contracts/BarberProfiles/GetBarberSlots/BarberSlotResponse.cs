namespace BarberSearch.Contracts.BarberProfiles.GetBarberSlots;

/// <summary>
/// Один свободный слот барбера.
/// </summary>
/// <remarks>
/// Границы слота — UTC-инстанты. Локальное время салона получается переводом
/// через <c>TimeZoneId</c> из <see cref="BarberSlotsResponse"/>; неявного сдвига
/// на сервере нет.
/// </remarks>
/// <param name="StartAt">Начало слота в UTC.</param>
/// <param name="EndAt">Окончание слота в UTC.</param>
public record BarberSlotResponse(
    DateTimeOffset StartAt,
    DateTimeOffset EndAt);
