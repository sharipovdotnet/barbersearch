using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Photos.Admin;

/// <summary>
/// Курсор keyset-пагинации админских очередей фотографий: последняя пара
/// <c>(submittedAt, id)</c>.
/// </summary>
/// <remarks>
/// Обе очереди читаются FIFO, поэтому направление в курсоре не кодируется: у аватаров
/// второй ключ — <c>userId</c> (<c>avatar_submitted_at</c>), у портфолио — <c>photoId</c>
/// (<c>created_at</c>). Содержимое — деталь реализации, клиент передаёт строку обратно
/// как есть; в публичном контракте видны только код ошибки <c>admin.photos.cursor</c>
/// и непрозрачная строка.
/// <para>
/// Время хранится в UTC и форматируется в invariant culture, как у остальных курсоров
/// (<c>AdminBarberCursor</c>, <c>AdminUserCursor</c>).
/// </para>
/// </remarks>
/// <param name="SubmittedAt">Время поступления последнего кадра страницы.</param>
/// <param name="Id">Идентификатор последней строки страницы.</param>
public sealed record AdminPhotoCursor(DateTimeOffset SubmittedAt, Guid Id)
{
    /// <summary>
    /// Публичный код ошибки «курсор очереди фотографий не разбирается».
    /// </summary>
    private const string INVALID_CURSOR_CODE = "admin.photos.cursor";

    /// <summary>
    /// Кодирует курсор в непрозрачную строку.
    /// </summary>
    public string Encode()
        => CursorCodec.Encode(
            CursorCodec.VERSION,
            CursorCodec.Format(SubmittedAt),
            Id.ToString(CursorCodec.GUID_FORMAT));

    /// <summary>
    /// Декодирует курсор. Битый курсор — <c>Error.Validation</c> (400) с кодом
    /// <c>admin.photos.cursor</c>.
    /// </summary>
    /// <param name="cursor">Строка курсора из query string.</param>
    public static Result<AdminPhotoCursor, Failure> Decode(string cursor)
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
            || !CursorCodec.TryParseTimestamp(parts[1], out var submittedAt)
            || !Guid.TryParseExact(parts[2], CursorCodec.GUID_FORMAT, out var id)
            || id == Guid.Empty)
        {
            return Invalid();
        }

        return new AdminPhotoCursor(submittedAt.ToUniversalTime(), id);
    }

    private static Failure Invalid()
        => new(Error.Validation(
            INVALID_CURSOR_CODE,
            Messages.CURSOR_INVALID,
            "cursor"));
}
