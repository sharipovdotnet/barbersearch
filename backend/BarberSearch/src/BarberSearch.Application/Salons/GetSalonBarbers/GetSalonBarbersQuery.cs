using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Salons.GetSalonBarbers;

namespace BarberSearch.Application.Salons.GetSalonBarbers;

/// <summary>
/// Барберы, прикреплённые к салону.
/// </summary>
/// <remarks>
/// Чтение доступно владельцу салона и администратору: состав салона — рабочий
/// инструмент владельца, а не публичная витрина. Публичная карточка барбера
/// по-прежнему показывает только активные и подтверждённые профили.
/// </remarks>
/// <param name="SalonId">Идентификатор салона.</param>
public record GetSalonBarbersQuery(Guid SalonId)
    : IQuery<IReadOnlyList<SalonBarberResponse>>;
