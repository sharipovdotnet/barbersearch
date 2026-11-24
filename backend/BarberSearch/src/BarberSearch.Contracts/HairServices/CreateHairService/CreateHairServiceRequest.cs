using BarberSearch.Domain.HairServices;

namespace BarberSearch.Contracts.HairServices.CreateHairService;

/// <summary>
/// Данные запроса на создание услуги каталога.
/// </summary>
/// <remarks>
/// Категория берётся из домена (<see cref="ServiceCategory"/>) — это словарь предметной области.
/// Длительность по умолчанию барбер может переопределить у себя.
/// </remarks>
public record CreateHairServiceRequest(
    string Name,
    ServiceCategory Category,
    int DefaultDurationMinutes,
    string? Description);
