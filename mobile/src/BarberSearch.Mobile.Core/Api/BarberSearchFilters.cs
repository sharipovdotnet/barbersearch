using BarberSearch.Domain.BarberProfiles.Enums;

namespace BarberSearch.Mobile.Core.Api;

/// <summary>
/// Фильтры публичного поиска барберов: те же шесть, что на витрине веба.
/// </summary>
/// <remarks>
/// <para>
/// Набор повторяет <c>frontend/src/lib/barber-search.ts</c>: город, дата, язык, специализация,
/// услуга справочника и дополнительная услуга. Пустое значение (<c>null</c> или пустая строка)
/// означает «фильтра нет», и клиент вообще не передаёт такой параметр.
/// </para>
/// <para>
/// Запись неизменяема: экран хранит снимок отправленных фильтров, поэтому подсказка пустой
/// выдачи описывает именно тот запрос, который вернул ноль строк, а не то, что пользователь
/// уже успел поправить в форме.
/// </para>
/// </remarks>
public sealed record BarberSearchFilters
{
    /// <summary>
    /// Пустой набор: ни один фильтр не задан.
    /// </summary>
    public static BarberSearchFilters Empty { get; } = new();

    /// <summary>
    /// Город салона; пустая строка — без фильтра. Сервер сравнивает город точно, поэтому
    /// клиент только обрезает пробелы и не меняет регистр.
    /// </summary>
    public string City { get; init; } = string.Empty;

    /// <summary>
    /// Календарная дата, на которую у барбера есть рабочий день; <c>null</c> — без фильтра.
    /// </summary>
    public DateOnly? Date { get; init; }

    /// <summary>
    /// Специализация справочника; <c>null</c> — без фильтра.
    /// </summary>
    public Guid? SpecializationId { get; init; }

    /// <summary>
    /// Услуга справочника (<c>hair_services.id</c>); <c>null</c> — без фильтра. Это не
    /// <c>barberServiceId</c> из брони.
    /// </summary>
    public Guid? HairServiceId { get; init; }

    /// <summary>
    /// Язык общения; <c>null</c> — без фильтра. Один язык, не мультивыбор.
    /// </summary>
    public Language? Language { get; init; }

    /// <summary>
    /// Подстрока дополнительной услуги; пустая строка — без фильтра. Сервер сравнивает её
    /// без учёта регистра с любым элементом списка дополнительных услуг барбера.
    /// </summary>
    public string AdditionalService { get; init; } = string.Empty;
}
