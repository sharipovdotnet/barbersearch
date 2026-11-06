using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Salons.RemoveSalonBarber;

/// <summary>
/// Открепление барбера от салона владельцем салона.
/// </summary>
/// <remarks>
/// Салон берётся из маршрута, профиль барбера — тоже. Владельца салона убрать
/// нельзя: его привязка создана вместе с салоном и держит владение рабочим.
/// </remarks>
/// <param name="SalonId">Салон, от которого открепляют барбера.</param>
/// <param name="BarberProfileId">Открепляемый профиль барбера.</param>
public record RemoveSalonBarberCommand(Guid SalonId, Guid BarberProfileId) : ICommand;
