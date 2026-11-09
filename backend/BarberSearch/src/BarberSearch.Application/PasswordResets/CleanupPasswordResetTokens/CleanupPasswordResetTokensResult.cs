namespace BarberSearch.Application.PasswordResets.CleanupPasswordResetTokens;

/// <summary>
/// Результат одного прогона уборки: сколько строк удалено.
/// </summary>
/// <param name="Deleted">Количество удалённых строк.</param>
public sealed record CleanupPasswordResetTokensResult(int Deleted);
