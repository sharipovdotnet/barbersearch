using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Application.BarberProfiles.SearchBarbers;

/// <summary>
/// Запрос поиска барберов с фильтрами и keyset-пагинацией.
/// </summary>
/// <remarks>
/// Пустые фильтры (<c>null</c>, пустая строка, <see cref="Guid.Empty"/>) означают
/// «не фильтровать» и не являются ошибкой. Курсор — непрозрачная строка из предыдущего
/// ответа; битый курсор даёт 400, а не 500.
/// </remarks>
/// <param name="City">Город салона.</param>
/// <param name="SpecializationId">Идентификатор специализации.</param>
/// <param name="HairServiceId">
/// Идентификатор услуги справочника (<c>hair_services.id</c>), а не строки
/// «услуга барбера» (R-20).
/// </param>
/// <param name="Date">Дата, на которую барбер работает.</param>
/// <param name="Language">
/// Язык общения именем enum (<c>Ru</c>, <c>ru</c>); пустая строка — без фильтра,
/// неизвестное значение — <c>400</c>.
/// </param>
/// <param name="Cursor">Курсор следующей страницы.</param>
/// <param name="PageSize">Размер страницы; <c>null</c> — значение по умолчанию.</param>
/// <param name="AdditionalService">
/// Строка поиска по дополнительным услугам: пустая строка — без фильтра, иначе
/// регистронезависимая подстрока любого элемента списка. Параметр добавлен в конец,
/// чтобы существующие вызовы записи не переставляли курсор и размер страницы.
/// </param>
public record SearchBarbersQuery(
    string? City,
    Guid? SpecializationId,
    Guid? HairServiceId,
    DateOnly? Date,
    string? Language,
    string? Cursor,
    int? PageSize,
    string? AdditionalService = null) : IQuery<PagedResponse<BarberSearchItemResponse>>;
