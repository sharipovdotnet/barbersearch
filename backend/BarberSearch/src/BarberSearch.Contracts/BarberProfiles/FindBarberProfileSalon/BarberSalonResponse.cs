namespace BarberSearch.Contracts.BarberProfiles.FindBarberProfileSalon;

/// <summary>
/// Ответ на запрос салона внутри профиля барбера.
/// </summary>
/// <remarks>
/// Доменная сущность наружу не отдаётся: у <c>BarberSalon</c> есть навигация на профиль,
/// и сериализация агрегата замкнула бы граф в цикл, выдав при этом весь профиль.
/// Здесь только данные пары (барбер, салон) вместе с её расписанием.
/// </remarks>
public record BarberSalonResponse(
    Guid Id,
    Guid BarberProfileId,
    Guid SalonId,
    bool IsPrimary,
    IReadOnlyList<WorkingScheduleResponse> WorkingSchedules);
