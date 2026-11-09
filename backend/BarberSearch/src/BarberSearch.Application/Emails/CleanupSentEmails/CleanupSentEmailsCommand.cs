using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Emails.CleanupSentEmails;

/// <summary>
/// Команда уборки обработанных строк outbox.
/// </summary>
/// <remarks>
/// Параметров нет: ретенция приходит из конфигурации (<c>EmailOutbox:RetentionDays</c>).
/// Команду вызывает фоновый воркер не чаще раза в сутки.
/// </remarks>
public sealed record CleanupSentEmailsCommand : ICommand;
