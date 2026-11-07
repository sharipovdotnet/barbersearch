using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Notifications;

/// <summary>
/// Keyset-курсор инбокса уведомлений: последняя пара <c>(created_at, id)</c> страницы.
/// </summary>
/// <remarks>
/// <para>
/// Инбокс всегда читается свежими сверху, поэтому направление в курсоре не кодируется:
/// клиент передаёт строку обратно как есть. Второй ключ — <see cref="Id"/>: тик воркера
/// создаёт несколько уведомлений в одну секунду, и без него страницы теряли бы или
/// дублировали строки.
/// </para>
/// <para>
/// Формат — общий <see cref="CursorCodec"/> с меткой области <c>barber.notifications</c>:
/// курсор другого списка имеет ту же форму, и без метки он молча применился бы к чужому
/// порядку вместо ошибки. Битые, старые и чужие курсоры отвергаются кодом
/// <c>notification.cursor.invalid</c>.
/// </para>
/// </remarks>
/// <param name="CreatedAt">Момент создания последней строки страницы.</param>
/// <param name="Id">Идентификатор последней строки страницы.</param>
public sealed record BarberNotificationCursor(DateTimeOffset CreatedAt, Guid Id)
{
    /// <summary>
    /// Публичный код ошибки «курсор инбокса не разбирается»; <c>400</c>.
    /// </summary>
    private const string INVALID_CURSOR_CODE = "notification.cursor.invalid";

    /// <summary>
    /// Метка области курсора: отличает инбокс уведомлений от броней, журнала и поиска.
    /// </summary>
    private const string SCOPE = "barber.notifications";

    /// <summary>
    /// Число частей полезной нагрузки: версия, область, момент создания и идентификатор.
    /// </summary>
    private const int PART_COUNT = 4;

    /// <summary>
    /// Кодирует курсор в непрозрачную строку.
    /// </summary>
    public string Encode()
        => CursorCodec.Encode(
            CursorCodec.VERSION,
            SCOPE,
            CursorCodec.Format(CreatedAt),
            Id.ToString(CursorCodec.GUID_FORMAT));

    /// <summary>
    /// Декодирует курсор. Битый, старый или чужой курсор — <c>Error.Validation</c> (400).
    /// </summary>
    /// <param name="cursor">Строка курсора из query string.</param>
    public static Result<BarberNotificationCursor, Failure> Decode(string cursor)
    {
        var partsResult = CursorCodec.Decode(cursor, PART_COUNT, INVALID_CURSOR_CODE);
        if (partsResult.IsFailure)
        {
            return partsResult.Error;
        }

        var parts = partsResult.Value;

        if (parts[0] != CursorCodec.VERSION
            || parts[1] != SCOPE
            || !CursorCodec.TryParseTimestamp(parts[2], out var createdAt)
            || !Guid.TryParseExact(parts[3], CursorCodec.GUID_FORMAT, out var id)
            || id == Guid.Empty)
        {
            return Invalid();
        }

        return new BarberNotificationCursor(createdAt.ToUniversalTime(), id);
    }

    private static Failure Invalid()
        => new(Error.Validation(
            INVALID_CURSOR_CODE,
            Messages.CURSOR_INVALID,
            "cursor"));
}
