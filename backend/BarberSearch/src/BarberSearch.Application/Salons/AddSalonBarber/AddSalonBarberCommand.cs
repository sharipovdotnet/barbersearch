using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Salons.AddSalonBarber;

/// <summary>
/// Прикрепление барбера к салону владельцем салона.
/// </summary>
/// <remarks>
/// Салон берётся из маршрута, профиль — из тела; тело не может подменить салон.
/// Профиль должен существовать и быть активным; повторная привязка — <c>409</c>.
/// </remarks>
/// <param name="SalonId">Салон, к которому прикрепляют барбера.</param>
/// <param name="BarberProfileId">Прикрепляемый профиль барбера.</param>
public record AddSalonBarberCommand(Guid SalonId, Guid BarberProfileId) : ICommand;
