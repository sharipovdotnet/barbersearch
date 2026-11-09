using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Emails.DispatchPendingEmails;

/// <summary>
/// Команда тика диспетчера: забрать пачку готовых писем из outbox и отправить их.
/// </summary>
/// <remarks>
/// Параметров нет: тик обслуживает очередь целиком, а размер пачки и предел попыток приходят
/// из конфигурации (<c>EmailOutbox</c>). Команду вызывает фоновый воркер.
/// </remarks>
public sealed record DispatchPendingEmailsCommand : ICommand;
