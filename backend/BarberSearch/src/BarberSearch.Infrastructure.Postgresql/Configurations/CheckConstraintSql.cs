using System.Globalization;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Строит SQL-фрагменты check-ограничений и фильтров индексов из перечислений и констант,
/// чтобы числовые значения не задавались в конфигурациях EF Core магическими числами.
/// </summary>
/// <remarks>
/// Значения берутся из <see cref="Enum.GetValues{TEnum}"/>, поэтому новое значение перечисления
/// автоматически попадает в ограничение. Готовые строки должны совпадать с уже применёнными
/// миграциями посимвольно: иное написание EF Core увидит как изменение модели.
/// </remarks>
internal static class CheckConstraintSql
{
    /// <summary>
    /// Диапазон значений перечисления: <c>column BETWEEN {min} AND {max}</c>.
    /// </summary>
    public static string EnumRange<TEnum>(string column)
        where TEnum : struct, Enum
    {
        var numbers = Enum.GetValues<TEnum>().Select(ToNumber).ToArray();

        return FormattableString.Invariant(
            $"{column} BETWEEN {numbers.Min()} AND {numbers.Max()}");
    }

    /// <summary>
    /// Все значения перечисления: <c>column IN ({value}, ...)</c> в порядке возрастания.
    /// </summary>
    public static string EnumIn<TEnum>(string column)
        where TEnum : struct, Enum
        => EnumIn(column, Enum.GetValues<TEnum>());

    /// <summary>
    /// Заданный набор значений перечисления: <c>column IN ({value}, ...)</c> — например,
    /// активные статусы брони. Порядок аргументов сохраняется.
    /// </summary>
    public static string EnumIn<TEnum>(string column, params TEnum[] values)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Length == 0)
        {
            throw new ArgumentException("At least one enum value is required.", nameof(values));
        }

        var numbers = values.Select(
            value => ToNumber(value).ToString(CultureInfo.InvariantCulture));

        return $"{column} IN ({string.Join(", ", numbers)})";
    }

    /// <summary>
    /// Числовой диапазон: <c>column BETWEEN {min} AND {max}</c> с двумя знаками после точки
    /// (формат <c>0.00</c>), как в уже применённых миграциях.
    /// </summary>
    public static string DecimalRange(string column, decimal min, decimal max)
    {
        var minText = min.ToString("0.00", CultureInfo.InvariantCulture);
        var maxText = max.ToString("0.00", CultureInfo.InvariantCulture);

        return $"{column} BETWEEN {minText} AND {maxText}";
    }

    private static long ToNumber<TEnum>(TEnum value)
        where TEnum : struct, Enum
        => Convert.ToInt64(value, CultureInfo.InvariantCulture);
}
