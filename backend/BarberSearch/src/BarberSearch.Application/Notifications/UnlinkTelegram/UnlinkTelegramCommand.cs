using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Notifications.UnlinkTelegram;

/// <summary>
/// Команда отвязки чата Telegram.
/// </summary>
/// <remarks>
/// Параметров нет: отвязывается чат текущего барбера. Операция идемпотентна — повторный
/// вызов (например, двойной клик) отвечает успехом, а не ошибкой.
/// </remarks>
public sealed record UnlinkTelegramCommand : ICommand;
