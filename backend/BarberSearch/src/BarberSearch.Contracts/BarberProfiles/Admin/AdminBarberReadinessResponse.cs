namespace BarberSearch.Contracts.BarberProfiles.Admin;

/// <summary>
/// Чек-лист готовности профиля для админской карточки.
/// </summary>
/// <remarks>
/// Это подсказка, а не блокировка: подтвердить профиль можно и с минусами, но администратор
/// видит, чего не хватает. Счётчики читаются отдельными подзапросами, поэтому чек-лист
/// не тянет дочерние коллекции агрегата в память.
/// </remarks>
/// <param name="IsProfileFilled">Заполнены ли описание и опыт работы.</param>
/// <param name="IsEmailVerified">Подтверждён ли email владельца профиля.</param>
/// <param name="ServicesCount">Сколько услуг предлагает барбер.</param>
/// <param name="SalonsCount">В скольких салонах работает барбер.</param>
/// <param name="ScheduleDaysCount">Сколько различных рабочих дней недели в расписании.</param>
/// <param name="PhotosCount">Сколько фотографий в портфолио.</param>
public record AdminBarberReadinessResponse(
    bool IsProfileFilled,
    bool IsEmailVerified,
    int ServicesCount,
    int SalonsCount,
    int ScheduleDaysCount,
    int PhotosCount);
