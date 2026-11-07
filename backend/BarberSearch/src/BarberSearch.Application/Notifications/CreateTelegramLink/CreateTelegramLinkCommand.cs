using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Notifications.CreateTelegramLink;

namespace BarberSearch.Application.Notifications.CreateTelegramLink;

/// <summary>
/// Команда выдачи одноразового кода привязки Telegram.
/// </summary>
/// <remarks>
/// Параметров нет: код выдаётся текущему барберу из сессии. Операция не привязывает чат
/// сама — чат привяжет вебхук бота, когда барбер откроет ссылку.
/// </remarks>
public sealed record CreateTelegramLinkCommand : ICommand;
