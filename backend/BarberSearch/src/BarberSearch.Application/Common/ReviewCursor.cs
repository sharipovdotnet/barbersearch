using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Common;

/// <summary>
/// Курсор keyset-пагинации списка отзывов: последняя пара <c>(created_at, id)</c> страницы.
/// </summary>
/// <remarks>
/// Курсор хранит только пару <c>(created_at, id)</c>; направление задаёт запрос: карточка
/// барбера читает <c>created_at DESC, id</c>, очередь модерации — <c>created_at ASC, id</c>.
/// Время хранится в UTC и форматируется в invariant culture; содержимое курсора — деталь
/// реализации, клиент передаёт строку обратно как есть.
/// </remarks>
/// <param name="CreatedAt">Время создания последнего отзыва страницы.</param>
/// <param name="Id">Идентификатор последнего отзыва страницы.</param>
public sealed record ReviewCursor(DateTimeOffset CreatedAt, Guid Id)
{
    /// <summary>
    /// Кодирует курсор в непрозрачную строку.
    /// </summary>
    public string Encode()
        => CursorCodec.Encode(
            "1",
            CursorCodec.Format(CreatedAt),
            Id.ToString("D"));

    /// <summary>
    /// Декодирует курсор. Битый курсор — <c>Error.Validation</c> (400).
    /// </summary>
    /// <param name="cursor">Строка курсора из query string.</param>
    public static Result<ReviewCursor, Failure> Decode(string cursor)
    {
        var partsResult = CursorCodec.Decode(cursor, 3, "review.cursor.invalid");
        if (partsResult.IsFailure)
        {
            return partsResult.Error;
        }

        var parts = partsResult.Value;

        if (parts[0] != "1"
            || !CursorCodec.TryParseTimestamp(parts[1], out var createdAt)
            || !Guid.TryParseExact(parts[2], "D", out var id)
            || id == Guid.Empty)
        {
            return Invalid();
        }

        return new ReviewCursor(createdAt.ToUniversalTime(), id);
    }

    private static Failure Invalid()
        => new(Error.Validation(
            "review.cursor.invalid",
            "The pagination cursor is invalid.",
            "cursor"));
}
