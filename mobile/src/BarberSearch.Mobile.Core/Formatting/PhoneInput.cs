namespace BarberSearch.Mobile.Core.Formatting;

/// <summary>
/// Результат нормализации телефона: текст поля и позиция курсора в нём.
/// </summary>
/// <param name="Text">Текст, который должен стоять в поле.</param>
/// <param name="CaretPosition">Позиция курсора сразу после нормализованного текста.</param>
public readonly record struct PhoneInputEdit(string Text, int CaretPosition);

/// <summary>
/// Ввод телефона в формах: дописывает код страны, пока человек набирает номер.
/// </summary>
/// <remarks>
/// <para>
/// Правило одно и то же для любой привычной записи российского мобильного: набранное
/// <c>9991234567</c> превращается в <c>+79991234567</c>, локальная <c>8 (999) 123-45-67</c>
/// — в <c>+7 (999) 123-45-67</c>, а <c>7 999 123-45-67</c> получает недостающий <c>+</c>.
/// Уже начатая запись с <c>+</c> не меняется: человек мог вставить номер из буфера или
/// стереть цифру в середине, и переформатирование переставило бы курсор.
/// </para>
/// <para>
/// Пустая строка и строка из пробелов остаются как есть: «номера ещё нет» — это состояние
/// формы, а не номер, и обязательность решает вызывающий код. Разделители внутри номера
/// сохраняются, поэтому <c>+7 (999) 123-45-67</c> остаётся читаемым.
/// </para>
/// </remarks>
public static class PhoneInput
{
    /// <summary>
    /// Код страны, который дописывается к номеру без него.
    /// </summary>
    public const string CountryCode = "+7";

    /// <summary>
    /// Начало российского мобильного номера: код страны и обязательная первая цифра <c>9</c>.
    /// </summary>
    public const string MobilePrefix = "+79";

    /// <summary>
    /// Длина <see cref="MobilePrefix"/> в символах: столько же занимает нестираемое начало поля.
    /// </summary>
    public const int MobilePrefixLength = 3;

    /// <summary>
    /// Сколько цифр национальной части помещается в номер: две из одиннадцати уже заняты
    /// кодом страны и обязательной девяткой.
    /// </summary>
    private const int NationalDigitsLength = 9;

    /// <summary>
    /// Дописывает <c>+7</c>, если человек начал номер с национальной части.
    /// </summary>
    /// <param name="value">Текст поля телефона.</param>
    /// <returns>Текст с кодом страны либо исходное значение, если оно пустое.</returns>
    public static string EnsureCountryCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value ?? string.Empty;
        }

        // Ведущие пробелы ничего не значат для номера и только мешают распознать первую
        // цифру; хвостовые остаются, чтобы набор с разделителем не дёргал курсор.
        var number = value.TrimStart();

        if (number.StartsWith('+'))
        {
            return number;
        }

        return number[0] switch
        {
            // «8 (999) …» — та же национальная запись, только с местной восьмёркой.
            '8' => string.Concat(CountryCode, number[1..]),

            // «7 999 …» — код страны без плюса.
            '7' => string.Concat('+', number),

            // «9991234567» — самая частая запись: дописываем код страны и получаем +79…
            _ => string.Concat(CountryCode, number),
        };
    }

    /// <summary>
    /// Приводит мобильный номер к скелету <c>+79XXXXXXXXX</c> и возвращает позицию курсора.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Поле «Телефон» на телефоне — обычный <c>Entry</c> с телефонной клавиатурой, без маски:
    /// разделители только мешали бы курсору, поэтому из значения берутся одни цифры. Первая
    /// цифра <c>7</c> или <c>8</c> — это код страны или местная восьмёрка, первая <c>9</c> —
    /// обязательное начало мобильного, остальные девять цифр — национальная часть. В поле
    /// всегда стоит начало настоящего номера, поэтому <c>+79</c> нельзя стереть: пустое
    /// значение нормализуется в него же.
    /// </para>
    /// <para>
    /// Курсор пересчитывается по числу цифр слева от него, а не по длине строки: после
    /// подстановки начала или отбрасывания разделителей старый индекс указывал бы в другое
    /// место. Если текст уже нормализован, курсор не трогается — иначе набор в конце номера
    /// дёргался бы.
    /// </para>
    /// </remarks>
    /// <param name="value">Текст поля; <c>null</c> считается пустым.</param>
    /// <param name="caretPosition">Позиция курсора в исходном тексте.</param>
    /// <returns>Нормализованный текст и позиция курсора в нём.</returns>
    public static PhoneInputEdit NormalizeMobile(string? value, int caretPosition)
    {
        var raw = value ?? string.Empty;
        var rest = ReadNationalDigits(raw, out var dropped);
        var normalized = string.Concat(MobilePrefix, rest);

        if (string.Equals(normalized, raw, StringComparison.Ordinal))
        {
            return new PhoneInputEdit(raw, caretPosition);
        }

        var caret = Math.Clamp(caretPosition, 0, raw.Length);
        var digitsBefore = 0;

        for (var index = 0; index < caret; index++)
        {
            if (IsAsciiDigit(raw[index]))
            {
                digitsBefore++;
            }
        }

        var restBefore = Math.Clamp(digitsBefore - dropped, 0, rest.Length);

        return new PhoneInputEdit(normalized, MobilePrefixLength + restBefore);
    }

    /// <summary>
    /// В значении есть цифры национальной части, а не только начало <c>+79</c>.
    /// </summary>
    /// <remarks>
    /// Проверка идёт по цифрам, а не по префиксу строки: значение могло прийти из модели без
    /// <c>+</c> («9991234567») или с местной восьмёркой. Одной цифры <c>9</c> мало — она уже
    /// входит в <see cref="MobilePrefix"/>.
    /// </remarks>
    /// <param name="value">Текст поля; <c>null</c> считается пустым.</param>
    /// <returns><c>true</c>, если после начала осталась хотя бы одна цифра.</returns>
    public static bool HasNationalDigits(string? value) =>
        ReadNationalDigits(value ?? string.Empty, out _).Length > 0;

    /// <summary>
    /// Оставляет цифры национальной части: отбрасывает код страны, местную восьмёрку и
    /// обязательную девятку, обрезая остаток до девяти цифр.
    /// </summary>
    /// <param name="raw">Исходный текст поля.</param>
    /// <param name="dropped">Сколько ведущих цифр отброшено как код страны и начало мобильного.</param>
    /// <returns>До девяти цифр национальной части.</returns>
    private static string ReadNationalDigits(string raw, out int dropped)
    {
        var digits = new char[raw.Length];
        var count = 0;

        foreach (var symbol in raw)
        {
            if (IsAsciiDigit(symbol))
            {
                digits[count++] = symbol;
            }
        }

        dropped = 0;
        var start = 0;

        if (count > 0 && digits[0] is '7' or '8')
        {
            start++;
            dropped++;
        }

        if (start < count && digits[start] == '9')
        {
            start++;
            dropped++;
        }

        return new string(digits, start, Math.Min(count - start, NationalDigitsLength));
    }

    /// <summary>
    /// Проверяет, что символ — цифра именно ASCII: телефонная клавиатура присылает только их,
    /// а <c>char.IsDigit</c> пропустил бы ещё и арабско-индийские.
    /// </summary>
    /// <param name="symbol">Проверяемый символ.</param>
    /// <returns><c>true</c> для <c>0</c>–<c>9</c>.</returns>
    private static bool IsAsciiDigit(char symbol) => symbol is >= '0' and <= '9';
}
