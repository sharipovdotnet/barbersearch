using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Сверяет специализации профиля с услугами: специализация живёт, пока у неё есть активная услуга.
/// </summary>
/// <remarks>
/// <para>
/// Активная услуга — источник истины: её <c>SpecializationId</c> обязан быть среди тегов профиля,
/// иначе фильтр поиска не найдёт мастера по этому направлению. Скрытая услуга источником не
/// считается: строка остаётся в прайсе ради истории записей, но клиентам её не видно, поэтому тег,
/// у которого все услуги скрыты, убирается.
/// </para>
/// <para>
/// Тег без единой строки услуги не трогается: его могли добавить вручную в веб-кабинете, и
/// мобильный раздел не должен стирать такую настройку. Поэтому удаляются только теги, которые
/// встречаются в прайсе, но не встречаются среди активных услуг.
/// </para>
/// <para>
/// Функция чистая: входные списки не меняются, повторные идентификаторы схлопываются, а пустой
/// <c>SpecializationId</c> пропускается — строка без специализации не создаёт и не убирает тег.
/// </para>
/// </remarks>
public static class BarberSpecializationSync
{
    /// <summary>
    /// Считает, какие специализации добавить и какие убрать.
    /// </summary>
    /// <param name="services">Прайс из собственной карточки: активные и скрытые строки.</param>
    /// <param name="specializations">Теги профиля из той же карточки.</param>
    /// <returns>Разница без повторов; пустая, если блок уже согласован с прайсом.</returns>
    public static BarberSpecializationChanges Compute(
        IReadOnlyList<BarberProfileServiceResponse> services,
        IReadOnlyList<BarberProfileSpecializationResponse> specializations)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(specializations);

        // Два набора услуг: все строки прайса и только активные. Первый решает, какие теги
        // вообще связаны с услугами, второй — какие из них должны остаться.
        var active = new HashSet<Guid>();
        var serviceBacked = new HashSet<Guid>();
        var activeOrder = new List<Guid>();

        foreach (var service in services)
        {
            var specializationId = service.SpecializationId;

            if (specializationId == Guid.Empty)
            {
                continue;
            }

            serviceBacked.Add(specializationId);

            if (service.IsActive && active.Add(specializationId))
            {
                activeOrder.Add(specializationId);
            }
        }

        var attached = new HashSet<Guid>();
        var toRemove = new List<Guid>();

        foreach (var specialization in specializations)
        {
            var specializationId = specialization.SpecializationId;

            if (specializationId == Guid.Empty || !attached.Add(specializationId))
            {
                continue;
            }

            // Тег без услуг не попадает в serviceBacked и остаётся: это ручная настройка.
            if (serviceBacked.Contains(specializationId) && !active.Contains(specializationId))
            {
                toRemove.Add(specializationId);
            }
        }

        var toAdd = new List<Guid>(activeOrder.Count);

        foreach (var specializationId in activeOrder)
        {
            if (!attached.Contains(specializationId))
            {
                toAdd.Add(specializationId);
            }
        }

        return new BarberSpecializationChanges(toAdd, toRemove);
    }
}
