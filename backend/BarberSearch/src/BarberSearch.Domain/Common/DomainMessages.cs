using BarberSearch.Domain.HairServices;

namespace BarberSearch.Domain.Common;

/// <summary>
/// Тексты доменных ошибок, которые повторяются в нескольких агрегатах или нужны за пределами
/// домена (Application, Infrastructure).
/// </summary>
/// <remarks>
/// Текст ошибки — часть наблюдаемого поведения (<c>Error.Message</c>), поэтому значения
/// остаются прежними: вынесены только повторяющиеся строки. Коды ошибок здесь не живут —
/// публичный код берётся из <c>ApiErrorCodes</c> либо из константы своего агрегата.
/// </remarks>
public static class DomainMessages
{
    /// <summary>
    /// Фотографии с указанным идентификатором нет в портфолио барбера.
    /// </summary>
    public const string PHOTO_NOT_FOUND =
        "The specified photo was not found in this artist's portfolio.";

    /// <summary>
    /// Идентификатор профиля барбера пуст.
    /// </summary>
    public const string BARBER_PROFILE_ID_REQUIRED = "Barber profile identifier cannot be empty.";

    /// <summary>
    /// Барбер не работает в указанном салоне.
    /// </summary>
    public const string BARBER_NOT_IN_SALON = "The barber in the specified salon does not work.";

    /// <summary>
    /// Цена не может быть отрицательной.
    /// </summary>
    public const string PRICE_NEGATIVE = "Price cannot be negative.";

    /// <summary>
    /// Имя специализации пусто.
    /// </summary>
    public const string SPECIALIZATION_NAME_REQUIRED = "Specialization name cannot be empty.";

    /// <summary>
    /// Конец интервала не позже его начала.
    /// </summary>
    public const string END_TIME_NOT_AFTER_START = "End time must be after start time.";

    /// <summary>
    /// Ключ идемпотентности пуст.
    /// </summary>
    public const string IDEMPOTENCY_KEY_REQUIRED = "Idempotency key cannot be empty.";

    /// <summary>
    /// Конец рабочего дня не позже его начала.
    /// </summary>
    public const string WORKING_SCHEDULE_END_TIME_NOT_AFTER_START =
        "Working schedule end time must be after its start time.";

    /// <summary>
    /// Подпись к фотографии длиннее <see cref="Constants.MAX_TEXT_LENGTH"/>.
    /// </summary>
    /// <remarks>
    /// <c>static readonly</c>, а не <c>const</c>: интерполяция с целочисленной константой
    /// не является константным выражением, поэтому значение вычисляется один раз при старте.
    /// </remarks>
    public static readonly string PHOTO_DESCRIPTION_TOO_LONG =
        $"Photo description cannot be longer than {Constants.MAX_TEXT_LENGTH} characters.";

    /// <summary>
    /// Значение категории услуги не определено в перечислении.
    /// </summary>
    /// <param name="category">Некорректная категория услуги.</param>
    public static string UnknownServiceCategory(ServiceCategory category)
        => $"Unknown service category value {(int)category}.";
}
