using BarberSearch.Domain.BarberProfiles.Enums;

namespace BarberSearch.Contracts.BarberProfiles.SearchBarbers;

/// <summary>
/// Строка результата поиска барберов.
/// </summary>
/// <remarks>
/// Это проекция, а не доменная сущность: коллекции салонов, услуг и специализаций
/// в поиске не загружаются. <see cref="Rating"/> = <c>null</c> означает «отзывов ещё нет»;
/// такие барберы идут после барберов с рейтингом.
/// </remarks>
/// <param name="Id">Идентификатор профиля барбера.</param>
/// <param name="DisplayName">Отображаемое имя мастера.</param>
/// <param name="Grade">Грейд барбера.</param>
/// <param name="YearsOfExperience">Стаж в годах.</param>
/// <param name="Rating">Средний рейтинг в шкале 1.00..5.00 либо <c>null</c>, если отзывов нет.</param>
/// <param name="ReviewsCount">Количество опубликованных отзывов.</param>
/// <param name="IsVerified">Подтверждён ли профиль администратором.</param>
/// <param name="PrimarySalonId">Основной активный салон либо <c>null</c>, если его нет.</param>
/// <param name="PrimarySalonName">Название основного активного салона либо <c>null</c>.</param>
/// <param name="City">Город основного активного салона либо <c>null</c>.</param>
public record BarberSearchItemResponse(
    Guid Id,
    string DisplayName,
    Grade Grade,
    int YearsOfExperience,
    decimal? Rating,
    int ReviewsCount,
    bool IsVerified,
    Guid? PrimarySalonId,
    string? PrimarySalonName,
    string? City);
