namespace BarberSearch.Contracts.Specializations.Admin;

/// <summary>
/// Строка админского справочника специализаций.
/// </summary>
/// <remarks>
/// Отдельный контракт от публичного <c>SpecializationResponse</c>: администратор видит
/// выключенные строки и число барберов, которые их используют. Ответ — полный справочник
/// без пагинации.
/// </remarks>
/// <param name="Id">Идентификатор специализации.</param>
/// <param name="Name">Название.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
/// <param name="IsActive">Видна ли строка в фильтрах витрины.</param>
/// <param name="UsageCount">
/// Сколько активных и подтверждённых профилей барберов указали специализацию: считается
/// <c>DISTINCT barber_profile_id</c> по <c>barber_specializations</c> с условием
/// «профиль активен и подтверждён». Выключенная строка остаётся на карточках барберов,
/// поэтому счётчик не обнуляется вместе с флагом.
/// </param>
public record AdminSpecializationResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int UsageCount);
