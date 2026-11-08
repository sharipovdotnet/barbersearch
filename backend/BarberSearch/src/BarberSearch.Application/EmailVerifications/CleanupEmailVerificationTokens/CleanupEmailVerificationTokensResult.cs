namespace BarberSearch.Application.EmailVerifications.CleanupEmailVerificationTokens;

/// <summary>
/// Результат одного прогона уборки: сколько строк удалено.
/// </summary>
/// <param name="Deleted">Количество удалённых строк.</param>
public sealed record CleanupEmailVerificationTokensResult(int Deleted);
