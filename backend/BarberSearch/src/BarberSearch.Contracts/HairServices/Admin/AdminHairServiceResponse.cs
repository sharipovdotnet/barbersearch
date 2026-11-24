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
/// <para>
/// Категории у услуги нет — она берётся из категории специализации. Название специализации
/// отдаётся рядом, чтобы админская таблица не делала второй запрос на подпись колонки.
/// Новые поля добавлены в конец записи с значениями по умолчанию.
/// </para>
/// </remarks>
/// <param name="Id">Идентификатор услуги справочника.</param>
/// <param name="Name">Название услуги.</param>
/// <param name="Category">Категория специализации услуги.</param>
/// <param name="DefaultDurationMinutes">Длительность по умолчанию, минуты.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
/// <param name="IsActive">Включена ли строка в публичные фильтры витрины.</param>
/// <param name="UsageCount">
/// Сколько активных и подтверждённых профилей барберов используют строку: считается
/// <c>DISTINCT barber_profile_id</c> по включённым связям <c>barber_services</c>, у которых
/// профиль активен и подтверждён. Неактивные и неподтверждённые профили не считаются.
/// </param>
/// <param name="SpecializationId">Специализация, к которой относится услуга.</param>
/// <param name="SpecializationName">Название специализации из справочника.</param>
public record AdminHairServiceResponse(
    Guid Id,
    string Name,
    ServiceCategory Category,
    int DefaultDurationMinutes,
    string? Description,
    bool IsActive,
    int UsageCount,
    Guid SpecializationId = default,
    string? SpecializationName = null);
