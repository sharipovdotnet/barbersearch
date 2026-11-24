using BarberSearch.Domain.HairServices;

namespace BarberSearch.Contracts.Specializations.GetSpecializations;

/// <summary>
/// Строка публичного справочника специализаций.
/// </summary>
/// <remarks>
/// Ответ — голый JSON-массив без пагинации: справочник маленький и полностью
/// кэшируем, витрине нужен весь список для выпадающего фильтра.
/// <para>
/// <see cref="Category"/> добавлена в конец записи со значением по умолчанию, чтобы
/// позиционные конструкции в мобильных тестах компилировались без правок.
/// </para>
/// </remarks>
/// <param name="Id">Идентификатор специализации.</param>
/// <param name="Name">Название для подписи в фильтре.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
/// <param name="Category">Категория услуг направления.</param>
public record SpecializationResponse(
    Guid Id,
    string Name,
    string? Description,
    ServiceCategory Category = ServiceCategory.Other);
