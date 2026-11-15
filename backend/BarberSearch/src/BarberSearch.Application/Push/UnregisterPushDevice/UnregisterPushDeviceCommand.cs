using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Push.UnregisterPushDevice;

namespace BarberSearch.Application.Push.UnregisterPushDevice;

/// <summary>
/// Команда снятия устройства с push-уведомлений.
/// </summary>
/// <remarks>
/// Владелец — текущий пользователь из сессии, поэтому <c>userId</c> в теле не принимается.
/// Операция идемпотентна: повтор и неизвестный токен отвечают успехом.
/// </remarks>
/// <param name="Request">Токен устройства.</param>
public sealed record UnregisterPushDeviceCommand(UnregisterPushDeviceRequest Request) : ICommand;
