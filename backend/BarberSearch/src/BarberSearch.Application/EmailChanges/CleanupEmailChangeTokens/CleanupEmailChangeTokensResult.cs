namespace BarberSearch.Application.EmailChanges.CleanupEmailChangeTokens;

/// <summary>
/// Результат одного прогона уборки: сколько строк удалено.
/// </summary>
/// <param name="Deleted">Количество удалённых строк.</param>
public sealed record CleanupEmailChangeTokensResult(int Deleted);
