using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Bookings.Admin;

/// <summary>
/// Курсор keyset-пагинации админского списка броней: последняя пара <c>(start_at, id)</c>
/// страницы.
/// </summary>
/// <remarks>
/// <para>
/// Список всегда читается свежими сверху, поэтому направление в курсоре не кодируется:
/// клиент передаёт строку обратно как есть. Второй ключ — <see cref="Id"/>: у разных
/// барберов визиты могут начинаться в одну минуту, и без него страницы теряли бы
/// или дублировали брони.
/// </para>
/// <para>
/// Формат — общий <see cref="CursorCodec"/>, но во второй части лежит область курсора
/// (<c>admin.bookings</c>): курсор другого списка имеет ту же форму, и без метки он молча
/// применился бы к чужому порядку вместо ошибки. Битые, старые и чужие курсоры отвергаются
/// с кодом <c>admin.bookings.cursor.invalid</c>.
/// </para>
/// </remarks>
/// <param name="StartAt">Начало визита последней брони страницы.</param>
/// <param name="Id">Идентификатор последней брони страницы.</param>
public sealed record AdminBookingCursor(DateTimeOffset StartAt, Guid Id)
{
    /// <summary>
    /// Публичный код ошибки «курсор списка броней не разбирается».
    /// </summary>
    private const string INVALID_CURSOR_CODE = "admin.bookings.cursor.invalid";

    /// <summary>
    /// Метка области курсора: отличает брони от журнала, пользователей и других списков.
    /// </summary>
    private const string SCOPE = "admin.bookings";

    /// <summary>
    /// Число частей полезной нагрузки: версия, область, начало визита и идентификатор.
    /// </summary>
    private const int PART_COUNT = 4;

    /// <summary>
    /// Кодирует курсор в непрозрачную строку.
    /// </summary>
    public string Encode()
        => CursorCodec.Encode(
            CursorCodec.VERSION,
            SCOPE,
            CursorCodec.Format(StartAt),
            Id.ToString(CursorCodec.GUID_FORMAT));

    /// <summary>
    /// Декодирует курсор. Битый, старый или чужой курсор — <c>Error.Validation</c> (400).
    /// </summary>
    /// <param name="cursor">Строка курсора из query string.</param>
    public static Result<AdminBookingCursor, Failure> Decode(string cursor)
    {
        var partsResult = CursorCodec.Decode(
            cursor,
            PART_COUNT,
            INVALID_CURSOR_CODE);
        if (partsResult.IsFailure)
        {
            return partsResult.Error;
        }

        var parts = partsResult.Value;

        if (parts[0] != CursorCodec.VERSION
            || parts[1] != SCOPE
            || !CursorCodec.TryParseTimestamp(parts[2], out var startAt)
            || !Guid.TryParseExact(parts[3], CursorCodec.GUID_FORMAT, out var id)
            || id == Guid.Empty)
        {
            return Invalid();
        }

        return new AdminBookingCursor(startAt.ToUniversalTime(), id);
    }

    private static Failure Invalid()
        => new(Error.Validation(
            INVALID_CURSOR_CODE,
            Messages.CURSOR_INVALID,
            "cursor"));
}
