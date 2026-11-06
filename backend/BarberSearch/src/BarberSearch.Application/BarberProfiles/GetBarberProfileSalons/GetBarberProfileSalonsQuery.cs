using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfileSalons;

namespace BarberSearch.Application.BarberProfiles.GetBarberProfileSalons;

/// <summary>
/// Список салонов, привязанных к профилю барбера.
/// </summary>
/// <remarks>
/// Экран «Мои салоны» показывает все привязки, а не только активные: срез #48 добавит
/// салоны «на проверке», и клиентская фильтрация по <c>isActive</c> спрятала бы их.
/// </remarks>
/// <param name="BarberProfileId">Профиль барбера.</param>
public record GetBarberProfileSalonsQuery(
    Guid BarberProfileId) : IQuery<IReadOnlyList<BarberSalonSummaryResponse>>;
