namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Точность и масштаб числовых колонок, объявленные в <c>HasPrecision</c>: имя константы
/// объясняет смысл пары чисел, а значение остаётся тем же, что и в применённых миграциях.
/// </summary>
internal static class ColumnPrecision
{
    /// <summary>Денежная сумма: <c>numeric(10, 2)</c>.</summary>
    public const int MONEY_PRECISION = 10;

    /// <summary>Знаков после точки у денежной суммы.</summary>
    public const int MONEY_SCALE = 2;

    /// <summary>Рейтинг барбера: <c>numeric(3, 2)</c>.</summary>
    public const int RATING_PRECISION = 3;

    /// <summary>Знаков после точки у рейтинга.</summary>
    public const int RATING_SCALE = 2;
}
