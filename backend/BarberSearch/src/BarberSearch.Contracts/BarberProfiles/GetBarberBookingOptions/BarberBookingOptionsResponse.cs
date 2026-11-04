using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;

namespace BarberSearch.Contracts.BarberProfiles.GetBarberBookingOptions;

/// <summary>
/// Данные формы «Записать клиента»: активные услуги и салоны самого барбера.
/// </summary>
/// <remarks>
/// Форма живёт в кабинете и не должна зависеть от публичной карточки: та отдаёт
/// только активный и подтверждённый профиль, а записать клиента барбер может и до
/// публикации. Поэтому профиль берётся из claim'а, а коллекции — из собственного
/// агрегата. Формы услуг и салонов совпадают с публичной карточкой, чтобы фронтенд
/// переиспользовал типы и проверки.
/// </remarks>
/// <param name="Services">Активные услуги барбера с ценой и длительностью.</param>
/// <param name="Salons">Активные салоны барбера вместе с недельным расписанием.</param>
public record BarberBookingOptionsResponse(
    IReadOnlyList<BarberProfileServiceResponse> Services,
    IReadOnlyList<BarberProfileSalonResponse> Salons);
