using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Users.Admin;

/// <summary>
/// Курсор keyset-пагинации админского списка пользователей: последняя пара
/// <c>(created_at, id)</c>.
/// </summary>
/// <remarks>
/// Список всегда читается свежими сверху, поэтому направление в курсоре не кодируется:
/// клиент передаёт строку обратно как есть и не разбирает её содержимое. Время хранится
/// в UTC и форматируется в invariant culture.
/// </remarks>
/// <param name="CreatedAt">Время создания последнего пользователя страницы.</param>
/// <param name="Id">Идентификатор последнего пользователя страницы.</param>
public sealed record AdminUserCursor(DateTimeOffset CreatedAt, Guid Id)
{
    /// <summary>
    /// Публичный код ошибки «курсор списка пользователей не разбирается».
    /// </summary>
    private const string INVALID_CURSOR_CODE = "admin.users.cursor.invalid";

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
    public static Result<AdminUserCursor, Failure> Decode(string cursor)
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

        return new AdminUserCursor(createdAt.ToUniversalTime(), id);
    }

    private static Failure Invalid()
        => new(Error.Validation(
            INVALID_CURSOR_CODE,
            Messages.CURSOR_INVALID,
            "cursor"));
}
