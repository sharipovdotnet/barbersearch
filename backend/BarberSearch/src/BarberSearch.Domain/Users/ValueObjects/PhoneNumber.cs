using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.Users.ValueObjects;

/// <summary>
/// Номер мобильного телефона, принадлежащий России.
/// </summary>
/// <remarks>
/// <para>
/// Объект-значение хранит только канонический вид <c>+7XXXXXXXXXX</c> (12 символов):
/// код страны <c>+7</c> и десять цифр национального номера. Канонический вид — часть
/// контракта хранения: по нему сравниваются номера и по нему же работает поиск.
/// </para>
/// <para>
/// На вход принимаются привычные формы записи: <c>+7 999 123-45-67</c>,
/// <c>8 (999) 123-45-67</c>, <c>7-999-123-45-67</c>, <c>9991234567</c>. Пробелы,
/// дефисы, круглые скобки и неразрывные пробелы игнорируются, ведущий <c>+</c>
/// допускается только первым символом. Всё остальное — буквы, другие знаки,
/// посторонний <c>+</c> — отклоняется.
/// </para>
/// <para>
/// Принадлежность к России проверяется по коду страны: принимается только <c>+7</c>
/// (форма с ведущей <c>8</c> — её локальная запись). Номер мобильный, поэтому первая
/// цифра национального номера должна быть <c>9</c>: городские номера
/// (<c>+7 495 …</c>, <c>+7 812 …</c>) и служебные диапазоны отклоняются.
/// </para>
/// <para>
/// Пустое значение не является номером: <see cref="Validate"/> считает <c>null</c>,
/// пустую и пробельную строку случаем «номера нет» и возвращает <c>null</c> без ошибки.
/// Обязательность решает вызывающий код; <see cref="Create"/> требует непустое значение.
/// </para>
/// </remarks>
public sealed class PhoneNumber : IEquatable<PhoneNumber>
{
    /// <summary>
    /// Код ошибки по умолчанию для недопустимого номера.
    /// </summary>
    public const string DEFAULT_ERROR_CODE = "user.phoneNumber";

    /// <summary>
    /// Код страны в каноническом виде.
    /// </summary>
    public const string PREFIX = "+7";

    /// <summary>
    /// Длина канонического значения: <c>+7</c> и десять цифр.
    /// </summary>
    public const int VALUE_LENGTH = 12;

    /// <summary>
    /// Верхняя граница длины исходной строки. Защищает разбор от заведомо мусорного
    /// ввода и совпадает с пределом телефона гостя в <see cref="Constants"/>.
    /// </summary>
    public const int MAX_INPUT_LENGTH = Constants.MAX_GUEST_PHONE_LENGTH;

    private const string REQUIRED_MESSAGE = "Phone number cannot be empty.";

    private const string INVALID_MESSAGE =
        "Phone number must be a Russian mobile number in the format +7 999 123-45-67.";

    private PhoneNumber(string value) => Value = value;

    /// <summary>
    /// Канонический номер: <c>+7</c> и десять цифр национального номера.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Создаёт номер из строки, требуя непустое значение.
    /// </summary>
    /// <param name="value">Исходная запись номера в любой поддерживаемой форме.</param>
    /// <param name="code">Код ошибки — часть контракта вызывающего сценария.</param>
    /// <param name="invalidField">Имя поля запроса для <c>invalidField</c> в ответе.</param>
    public static Result<PhoneNumber, Error> Create(
        string? value,
        string code = DEFAULT_ERROR_CODE,
        string invalidField = "phoneNumber")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Error.Validation(code, REQUIRED_MESSAGE, invalidField);
        }

        return TryNormalize(value, out var canonical)
            ? new PhoneNumber(canonical)
            : Error.Validation(code, INVALID_MESSAGE, invalidField);
    }

    /// <summary>
    /// Проверяет и нормализует необязательный номер.
    /// </summary>
    /// <remarks>
    /// Отсутствие номера — это <c>null</c>, пустая или пробельная строка: метод
    /// возвращает <c>null</c> без ошибки и <paramref name="phoneNumber"/> = <c>null</c>.
    /// Любая непустая строка обязана быть российским мобильным номером.
    /// </remarks>
    /// <param name="value">Исходная запись номера либо <c>null</c>.</param>
    /// <param name="code">Код ошибки — часть контракта вызывающего сценария.</param>
    /// <param name="invalidField">Имя поля запроса для <c>invalidField</c> в ответе.</param>
    /// <param name="phoneNumber">
    /// Канонический номер при успехе; <c>null</c>, если номер не задан или значение
    /// недопустимо.
    /// </param>
    /// <returns>Ошибка валидации либо <c>null</c>, если значение допустимо.</returns>
    public static Error? Validate(
        string? value,
        string code,
        string invalidField,
        out PhoneNumber? phoneNumber)
    {
        phoneNumber = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!TryNormalize(value, out var canonical))
        {
            return Error.Validation(code, INVALID_MESSAGE, invalidField);
        }

        phoneNumber = new PhoneNumber(canonical);

        return null;
    }

    /// <summary>
    /// Проверяет, что непустая строка является российским мобильным номером.
    /// </summary>
    /// <param name="value">Проверяемое значение; <c>null</c> и пустая строка — не номер.</param>
    public static bool IsValid(string? value)
        => !string.IsNullOrWhiteSpace(value) && TryNormalize(value, out _);

    /// <summary>
    /// Приводит поддерживаемую запись к каноническому виду <c>+7XXXXXXXXXX</c>.
    /// </summary>
    /// <param name="value">Непустая исходная строка.</param>
    /// <param name="canonical">Канонический номер при успехе.</param>
    private static bool TryNormalize(string value, out string canonical)
    {
        canonical = string.Empty;

        var trimmed = value.Trim();

        if (trimmed.Length is 0 or > MAX_INPUT_LENGTH)
        {
            return false;
        }

        // Собираем только цифры; разделители привычной записи пропускаем.
        // Знак «+» допустим единственным и только первым символом.
        var digits = new char[trimmed.Length];
        var count = 0;
        var hasPlus = false;

        for (var index = 0; index < trimmed.Length; index++)
        {
            var symbol = trimmed[index];

            if (char.IsAsciiDigit(symbol))
            {
                digits[count++] = symbol;

                continue;
            }

            if (symbol == '+')
            {
                if (index != 0)
                {
                    return false;
                }

                hasPlus = true;

                continue;
            }

            if (char.IsWhiteSpace(symbol) || symbol is '-' or '(' or ')')
            {
                continue;
            }

            return false;
        }

        if (count is < 10 or > 11)
        {
            return false;
        }

        // 11 цифр — это запись с кодом страны: «7…» или локальная «8…».
        // «+8…» — уже другой код страны, а не российский номер.
        var start = 0;

        if (count == 11)
        {
            if (digits[0] == '7' || (digits[0] == '8' && !hasPlus))
            {
                start = 1;
            }
            else
            {
                return false;
            }
        }
        else if (hasPlus)
        {
            // «+7» и всего десять цифр — в номере не хватает цифры.
            return false;
        }

        // Российские мобильные номера начинаются с 9: городские и служебные
        // диапазоны (495, 812, 800 …) этому правилу не удовлетворяют.
        if (digits[start] != '9')
        {
            return false;
        }

        canonical = string.Concat(PREFIX, new string(digits, start, count - start));

        return true;
    }

    /// <inheritdoc />
    public bool Equals(PhoneNumber? other)
        => other is not null &&
           string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is PhoneNumber other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    public static bool operator ==(PhoneNumber? left, PhoneNumber? right)
        => object.Equals(left, right);

    public static bool operator !=(PhoneNumber? left, PhoneNumber? right)
        => !object.Equals(left, right);
}
