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
    /// Публичный код ошибки «курсор списка броней не разбирается».
    /// </summary>
    private const string INVALID_CURSOR_CODE = "booking.cursor.invalid";

    /// <summary>
    /// Кодирует курсор в непрозрачную строку.
    /// </summary>
    public string Encode()
        => CursorCodec.Encode(
            CursorCodec.VERSION,
            CursorCodec.Format(StartAt),
            Id.ToString(CursorCodec.GUID_FORMAT));

    /// <summary>
    /// Декодирует курсор. Битый курсор — <c>Error.Validation</c> (400).
    /// </summary>
    /// <param name="cursor">Строка курсора из query string.</param>
    public static Result<BookingCursor, Failure> Decode(string cursor)
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
            || !CursorCodec.TryParseTimestamp(parts[1], out var startAt)
            || !Guid.TryParseExact(parts[2], CursorCodec.GUID_FORMAT, out var id)
            || id == Guid.Empty)
        {
            return Invalid();
        }

        return new BookingCursor(startAt.ToUniversalTime(), id);
    }

    private static Failure Invalid()
        => new(Error.Validation(
            INVALID_CURSOR_CODE,
            Messages.CURSOR_INVALID,
            "cursor"));
}
