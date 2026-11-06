using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.BarberRequests.Admin;

/// <summary>
/// Курсор keyset-пагинации администраторского списка заявок: последняя пара
/// <c>(created_at, id)</c> страницы.
/// </summary>
/// <remarks>
/// Направление задаёт фильтр: очередь <c>pending</c> читается <c>created_at ASC, id</c>,
/// список <c>all</c> — <c>created_at DESC, id</c>. Время хранится в UTC и форматируется
/// в invariant culture; содержимое — деталь реализации, клиент передаёт строку обратно как есть.
/// </remarks>
/// <param name="CreatedAt">Время подачи последней заявки страницы.</param>
/// <param name="Id">Идентификатор последней заявки страницы.</param>
public sealed record AdminBarberRequestCursor(DateTimeOffset CreatedAt, Guid Id)
{
    /// <summary>
    /// Публичный код ошибки «курсор списка заявок не разбирается».
    /// </summary>
    private const string INVALID_CURSOR_CODE = "barberRequest.list.cursor.invalid";

    /// <summary>
    /// Кодирует курсор в непрозрачную строку.
    /// </summary>
    public string Encode()
        => CursorCodec.Encode(
            CursorCodec.VERSION,
            CursorCodec.Format(CreatedAt),
            Id.ToString(CursorCodec.GUID_FORMAT));

    /// <summary>
    /// Декодирует курсор. Битый курсор — <c>Error.Validation</c> (400).
    /// </summary>
    /// <param name="cursor">Строка курсора из query string.</param>
    public static Result<AdminBarberRequestCursor, Failure> Decode(string cursor)
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
            || !CursorCodec.TryParseTimestamp(parts[1], out var createdAt)
            || !Guid.TryParseExact(parts[2], CursorCodec.GUID_FORMAT, out var id)
            || id == Guid.Empty)
        {
            return Invalid();
        }

        return new AdminBarberRequestCursor(createdAt.ToUniversalTime(), id);
    }

    private static Failure Invalid()
        => new(Error.Validation(
            INVALID_CURSOR_CODE,
            Messages.CURSOR_INVALID,
            "cursor"));
}
