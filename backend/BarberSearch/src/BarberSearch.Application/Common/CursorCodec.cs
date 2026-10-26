using System.Globalization;
using System.Text;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Common;

/// <summary>
/// Кодирование и декодирование непрозрачных keyset-курсоров.
/// </summary>
/// <remarks>
/// Формат — base64url от строки с частями, разделёнными <c>|</c>. Наружу курсор выглядит
/// как случайная строка: клиент не должен разбирать его содержимое и передаёт его обратно
/// без изменений. Числа кодируются в invariant culture, иначе курсор, выданный сервером
/// с одной культурой, не прочитался бы на сервере с другой.
/// </remarks>
internal static class CursorCodec
{
    /// <summary>
    /// Версия формата курсора. Меняется вместе с составом частей: старый курсор после
    /// этого отвергается, а не читается неправильно.
    /// </summary>
    public const string VERSION = "1";

    /// <summary>
    /// Число частей полезной нагрузки, общее для всех курсоров: версия, ключ и id.
    /// </summary>
    public const int PART_COUNT = 3;

    /// <summary>
    /// Формат <see cref="Guid"/> в текстовом виде: 32 hex-цифры и четыре дефиса.
    /// </summary>
    public const string GUID_FORMAT = "D";

    /// <summary>
    /// Разделитель частей полезной нагрузки. Внутри частей не встречается.
    /// </summary>
    private const char SEPARATOR = '|';

    /// <summary>
    /// Размер блока base64: до добавления padding длина строки должна быть кратна ему.
    /// </summary>
    private const int BASE64_BLOCK = 4;

    /// <summary>
    /// Верхняя граница длины курсора: защита от разбора произвольно большой строки.
    /// </summary>
    private const int MAX_CURSOR_LENGTH = 2048;

    /// <summary>
    /// Кодирует части курсора в base64url без завершающих символов <c>=</c>.
    /// </summary>
    /// <param name="parts">Части полезной нагрузки; пустая строка допустима для null-значения.</param>
    public static string Encode(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var payload = string.Join(SEPARATOR, parts);
        var bytes = Encoding.UTF8.GetBytes(payload);

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Декодирует курсор и проверяет число частей. Ошибка — <c>Error.Validation</c> (400),
    /// а не исключение: битый курсор приходит от клиента и не должен давать 500.
    /// </summary>
    /// <param name="cursor">Курсор из query string.</param>
    /// <param name="expectedParts">Ожидаемое число частей.</param>
    /// <param name="errorCode">Код ошибки для конкретного эндпоинта.</param>
    public static Result<IReadOnlyList<string>, Failure> Decode(
        string? cursor,
        int expectedParts,
        string errorCode)
    {
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > MAX_CURSOR_LENGTH)
        {
            return Invalid(errorCode);
        }

        try
        {
            var normalized = cursor
                .Replace('-', '+')
                .Replace('_', '/');

            var remainder = normalized.Length % BASE64_BLOCK;
            if (remainder != 0)
            {
                normalized += new string('=', BASE64_BLOCK - remainder);
            }

            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            var parts = payload.Split(SEPARATOR);

            return parts.Length == expectedParts
                ? parts
                : Invalid(errorCode);
        }
        catch (FormatException)
        {
            return Invalid(errorCode);
        }
        catch (ArgumentException)
        {
            return Invalid(errorCode);
        }
    }

    /// <summary>
    /// Форматирует число для курсора в invariant culture.
    /// </summary>
    public static string Format(decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Разбирает число из курсора в invariant culture.
    /// </summary>
    public static bool TryParseDecimal(string value, out decimal result)
        => decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out result);

    /// <summary>
    /// Форматирует момент времени для курсора в UTC и invariant culture.
    /// </summary>
    public static string Format(DateTimeOffset value)
        => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    /// <summary>
    /// Разбирает момент времени из курсора в invariant culture.
    /// </summary>
    public static bool TryParseTimestamp(string value, out DateTimeOffset result)
        => DateTimeOffset.TryParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out result);

    private static Failure Invalid(string errorCode)
        => new(Error.Validation(
            errorCode,
            Messages.CURSOR_INVALID,
            "cursor"));
}
