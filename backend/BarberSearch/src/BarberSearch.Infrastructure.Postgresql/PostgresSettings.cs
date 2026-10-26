namespace BarberSearch.Infrastructure.Postgresql;

/// <summary>
/// Эксплуатационные ограничения при работе с PostgreSQL.
/// </summary>
/// <remarks>
/// Таймауты — страховка от зависших транзакций: ожидание advisory-блокировки ограничено
/// <see cref="LOCK_TIMEOUT"/>, а выполнение любой команды — <see cref="COMMAND_TIMEOUT_SECONDS"/>.
/// Readiness-проверка БД ограничена собственным <c>HealthChecks:DatabaseTimeoutSeconds</c>
/// (5 секунд по умолчанию) и отменяется раньше общего таймаута команды, поэтому эти настройки
/// не конфликтуют.
/// </remarks>
public static class PostgresSettings
{
    /// <summary>
    /// Таймаут выполнения команды в секундах.
    /// </summary>
    public const int COMMAND_TIMEOUT_SECONDS = 30;

    /// <summary>
    /// Таймаут ожидания advisory-блокировки. Применяется через <c>SET LOCAL</c> и действует
    /// только до конца текущей транзакции; значение — секунды с единицей измерения,
    /// как его принимает PostgreSQL.
    /// </summary>
    public const string LOCK_TIMEOUT = "2s";
}
