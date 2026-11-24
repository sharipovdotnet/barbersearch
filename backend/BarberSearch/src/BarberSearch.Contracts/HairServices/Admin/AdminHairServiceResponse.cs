using BarberSearch.Domain.HairServices;

namespace BarberSearch.Contracts.HairServices.Admin;

/// <summary>
/// Строка админского справочника услуг.
/// </summary>
/// <remarks>
/// Отдельный контракт от публичного <c>HairServiceResponse</c>: администратор видит и
/// выключенные строки, поэтому в ответ входит <c>isActive</c>, а <c>usageCount</c> отвечает
/// на вопрос «сколько барберов сейчас пользуются этой услугой». Ответ — полный справочник,
/// а не страница: словарь маленький, а созданная строка должна быть видна сразу.
/// </remarks>
/// <param name="Id">Идентификатор услуги справочника.</param>
/// <param name="Name">Название услуги.</param>
/// <param name="Category">Категория; после создания не меняется.</param>
/// <param name="DefaultDurationMinutes">Длительность по умолчанию, минуты.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
/// <param name="IsActive">Включена ли строка в публичные фильтры витрины.</param>
/// <param name="UsageCount">
/// Сколько активных и подтверждённых профилей барберов используют строку: считается
/// <c>DISTINCT barber_profile_id</c> по включённым связям <c>barber_services</c>, у которых
/// профиль активен и подтверждён. Неактивные и неподтверждённые профили не считаются.
/// </param>
public record AdminHairServiceResponse(
    Guid Id,
    string Name,
    ServiceCategory Category,
    int DefaultDurationMinutes,
    string? Description,
    bool IsActive,
    int UsageCount);
