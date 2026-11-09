using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Common;

/// <summary>
/// Курсор keyset-пагинации очереди модерации: последняя пара <c>(updated_at, id)</c> страницы.
/// </summary>
/// <remarks>
/// <para>
/// Очередь упорядочена по моменту попадания в неё, а не по времени создания отзыва:
/// правка текста опубликованного отзыва возвращает его в очередь (R-35), и <c>created_at</c>
/// больше не описывает ожидание — иначе старый отзыв встал бы в начало очереди и в сводке
/// «ждал» бы месяцами.
/// </para>
/// <para>
/// Момент входа в очередь совпадает с <c>updated_at</c>: создание, скрытие администратором
/// и возврат правкой его ставят, публикация выводит строку из очереди. Направление задаёт
/// только запрос — очередь читается по возрастанию, опубликованные строки в неё не входят.
/// Время хранится в UTC; содержимое курсора — деталь реализации, клиент передаёт строку обратно
/// как есть.
/// </para>
/// </remarks>
/// <param name="AwaitingSince">Момент последнего попадания строки в очередь (UTC).</param>
/// <param name="Id">Идентификатор последнего отзыва страницы.</param>
public sealed record ModerationCursor(DateTimeOffset AwaitingSince, Guid Id)
{
    /// <summary>
    /// Публичный код ошибки «курсор очереди модерации не разбирается».
    /// </summary>
    private const string INVALID_CURSOR_CODE = "review.cursor.invalid";

    /// <summary>
    /// Кодирует курсор в непрозрачную строку.
    /// </summary>
    public string Encode()
        => CursorCodec.Encode(
            CursorCodec.VERSION,
            CursorCodec.Format(AwaitingSince),
            Id.ToString(CursorCodec.GUID_FORMAT));

    /// <summary>
    /// Декодирует курсор. Битый курсор — <c>Error.Validation</c> (400).
    /// </summary>
    /// <param name="cursor">Строка курсора из query string.</param>
    public static Result<ModerationCursor, Failure> Decode(string cursor)
    {
        var partsResult = CursorCodec.Decode(
            cursor,
            CursorCodec.PART_COUNT,
            INVALID_CURSOR_CODE);
        if (partsResult.IsFailure)
        {
            return partsResult.Error;
        }

        var parts = partsResult.Value;

        if (parts[0] != CursorCodec.VERSION
            || !CursorCodec.TryParseTimestamp(parts[1], out var awaitingSince)
            || !Guid.TryParseExact(parts[2], CursorCodec.GUID_FORMAT, out var id)
            || id == Guid.Empty)
        {
            return Invalid();
        }

        return new ModerationCursor(awaitingSince.ToUniversalTime(), id);
    }

    private static Failure Invalid()
        => new(Error.Validation(
            INVALID_CURSOR_CODE,
            Messages.CURSOR_INVALID,
            "cursor"));
}
