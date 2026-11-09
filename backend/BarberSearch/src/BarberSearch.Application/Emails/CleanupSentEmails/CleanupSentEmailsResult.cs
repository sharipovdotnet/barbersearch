namespace BarberSearch.Application.Emails.CleanupSentEmails;

/// <summary>
/// Итог уборки: сколько обработанных строк удалено.
/// </summary>
/// <param name="Deleted">Количество удалённых строк outbox.</param>
public sealed record CleanupSentEmailsResult(int Deleted);
