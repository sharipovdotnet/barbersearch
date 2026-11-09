namespace BarberSearch.Web.Emails;

/// <summary>
/// Итог одного тика диспетчера outbox.
/// </summary>
/// <param name="Sent">Сколько писем принял транспорт.</param>
/// <param name="Retried">Сколько попыток закончилось временной ошибкой и будет повторено.</param>
/// <param name="Failed">Сколько строк окончательно провалено.</param>
public sealed record EmailTickResult(int Sent, int Retried, int Failed)
{
    /// <summary>
    /// Пустой итог: воркер выключен или отправлять было нечего.
    /// </summary>
    public static EmailTickResult Empty { get; } = new(0, 0, 0);
}
