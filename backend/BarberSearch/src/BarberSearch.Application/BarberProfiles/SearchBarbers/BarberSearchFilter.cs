using BarberSearch.Application.Common;
using BarberSearch.Domain.BarberProfiles.Enums;

namespace BarberSearch.Application.BarberProfiles.SearchBarbers;

/// <summary>
/// Параметры поиска барберов после разбора query string и курсора.
/// </summary>
/// <remarks>
/// <see cref="Limit"/> — это <c>pageSize + 1</c>: лишняя строка нужна, чтобы отличить
/// «страница последняя» от «есть ещё одна», не выполняя отдельный COUNT.
/// </remarks>
/// <param name="City">Город салона; <c>null</c> — фильтр не задан.</param>
/// <param name="SpecializationId">Специализация; <c>null</c> — фильтр не задан.</param>
/// <param name="HairServiceId">
/// Услуга справочника (<c>hair_services.id</c>); <c>null</c> — фильтр не задан.
/// </param>
/// <param name="Date">Дата, на которую у барбера есть рабочий день; <c>null</c> — фильтр не задан.</param>
/// <param name="Language">Язык общения; <c>null</c> — фильтр не задан. Один язык, не мультивыбор.</param>
/// <param name="Cursor">Позиция продолжения выдачи; <c>null</c> — первая страница.</param>
/// <param name="Limit">Сколько строк прочитать из базы (pageSize + 1).</param>
public sealed record BarberSearchFilter(
    string? City,
    Guid? SpecializationId,
    Guid? HairServiceId,
    DateOnly? Date,
    Language? Language,
    BarberSearchCursor? Cursor,
    int Limit);
