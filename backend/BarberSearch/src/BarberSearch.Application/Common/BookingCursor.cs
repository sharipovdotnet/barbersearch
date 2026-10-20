using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Common;

/// <summary>
/// Курсор keyset-пагинации списка броней: последняя пара <c>(start_at, id)</c> страницы.
/// </summary>
/// <remarks>
/// Порядок — <c>start_at DESC, id</c>. Время хранится в UTC и форматируется в invariant
/// culture, чтобы курсор переживал смену культуры сервера. Содержимое курсора клиенту
/// знать не нужно: он передаёт строку обратно без изменений.
/// </remarks>
/// <param name="StartAt">Начало последней брони страницы.</param>
/// <param name="Id">Идентификатор последней брони страницы.</param>
public sealed record BookingCursor(DateTimeOffset StartAt, Guid Id)
{
    /// <summary>
    /// Кодирует курсор в непрозрачную строку.
    /// </summary>
    public string Encode()
        => CursorCodec.Encode(
            "1",
            CursorCodec.Format(StartAt),
            Id.ToString("D"));

    /// <summary>
    /// Декодирует курсор. Битый курсор — <c>Error.Validation</c> (400).
    /// </summary>
    /// <param name="cursor">Строка курсора из query string.</param>
    public static Result<BookingCursor, Failure> Decode(string cursor)
    {
        var partsResult = CursorCodec.Decode(cursor, 3, "booking.cursor.invalid");
        if (partsResult.IsFailure)
        {
            return partsResult.Error;
        }

        var parts = partsResult.Value;

        if (parts[0] != "1"
            || !CursorCodec.TryParseTimestamp(parts[1], out var startAt)
            || !Guid.TryParseExact(parts[2], "D", out var id)
            || id == Guid.Empty)
        {
            return Invalid();
        }

        return new BookingCursor(startAt.ToUniversalTime(), id);
    }

    private static Failure Invalid()
        => new(Error.Validation(
            "booking.cursor.invalid",
            "The pagination cursor is invalid.",
            "cursor"));
}
