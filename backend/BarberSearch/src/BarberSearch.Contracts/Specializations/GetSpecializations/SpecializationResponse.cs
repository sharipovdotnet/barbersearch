using BarberSearch.Domain.HairServices;

namespace BarberSearch.Contracts.Specializations.GetSpecializations;

/// <summary>
/// Строка публичного справочника специализаций.
/// </summary>
/// <remarks>
/// Ответ — голый JSON-массив без пагинации: справочник маленький и полностью
/// кэшируем, витрине нужен весь список для выпадающего фильтра.
/// </remarks>
/// <param name="Id">Идентификатор специализации.</param>
/// <param name="Name">Название для подписи в фильтре.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
/// <param name="Category">
/// Категория направления — категория большинства его активных услуг; ничья решается меньшим
/// числовым значением <see cref="ServiceCategory"/>, отсутствие активных услуг даёт
/// <see cref="ServiceCategory.Other"/>. Поле не хранится в БД, а выводится при чтении.
/// </param>
public record SpecializationResponse(
    Guid Id,
    string Name,
    string? Description,
    ServiceCategory Category);
