namespace BarberSearch.Web.Push;

/// <summary>
/// Итог одного тика воркера push-уведомлений.
/// </summary>
/// <remarks>
/// Счётчики нужны интеграционным тестам: они вызывают <c>RunTickAsync</c> напрямую и
/// проверяют, что тик создал и доставил ожидаемое. В журнал те же числа попадают одной
/// строкой <c>Information</c> на тик. <c>Created</c> считает только напоминания: событие
/// создания брони ставится в очередь обработчиком брони, а не воркером.
/// </remarks>
/// <param name="Created">Сколько напоминаний создано за тик.</param>
/// <param name="Sent">Сколько доставлено хотя бы на одно устройство.</param>
/// <param name="Skipped">Сколько пропущено окончательно.</param>
/// <param name="Failed">Сколько неудачных попыток записано (с повтором или уже исчерпанных).</param>
public sealed record PushTickResult(int Created, int Sent, int Skipped, int Failed)
{
    /// <summary>
    /// Пустой итог: воркер выключен или ни один шаг не выполнился.
    /// </summary>
    public static PushTickResult Empty { get; } = new(0, 0, 0, 0);
}
