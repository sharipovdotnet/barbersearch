using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Push.RegisterPushDevice;

namespace BarberSearch.Application.Push.RegisterPushDevice;

/// <summary>
/// Команда регистрации устройства для push-уведомлений.
/// </summary>
/// <remarks>
/// Владелец устройства — текущий пользователь из сессии, поэтому <c>userId</c> в теле
/// не принимается. Токен и платформа приходят из контракта; сам токен сервер не проверяет
/// на валидность у FCM — это выяснится первой отправкой.
/// </remarks>
/// <param name="Request">Токен устройства и платформа.</param>
public sealed record RegisterPushDeviceCommand(RegisterPushDeviceRequest Request) : ICommand;
