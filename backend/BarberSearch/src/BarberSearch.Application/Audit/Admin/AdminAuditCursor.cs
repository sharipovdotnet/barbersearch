using BarberSearch.Application.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Audit.Admin;

/// <summary>
/// Курсор keyset-пагинации журнала: последняя пара <c>(occurred_at, id)</c> страницы.
/// </summary>
/// <remarks>
/// <para>
/// Журнал всегда читается свежими сверху, поэтому направление в курсоре не кодируется:
/// клиент передаёт строку обратно как есть. Второй ключ — <see cref="Id"/>: строки одного
/// <c>SaveChanges</c> имеют одинаковый <c>occurred_at</c>, и без него страницы теряли бы
/// или дублировали записи.
/// </para>
/// <para>
/// Формат — общий <see cref="CursorCodec"/>, но во второй части лежит область курсора
/// (<c>admin.audit</c>): курсор другого списка имеет ту же форму, и без метки он молча
/// применился бы к чужому порядку вместо ошибки. Старые и чужие курсоры отвергаются
/// с кодом <c>admin.audit.cursor.invalid</c>.
/// </para>
/// </remarks>
/// <param name="OccurredAt">Момент последней строки страницы.</param>
/// <param name="Id">Идентификатор последней строки страницы.</param>
public sealed record AdminAuditCursor(DateTimeOffset OccurredAt, Guid Id)
{
    /// <summary>
    /// Публичный код ошибки «курсор журнала не разбирается».
    /// </summary>
    private const string INVALID_CURSOR_CODE = "admin.audit.cursor.invalid";

    /// <summary>
    /// Метка области курсора: отличает журнал от пользователей, отзывов и броней.
    /// </summary>
    private const string SCOPE = "admin.audit";

    /// <summary>
    /// Число частей полезной нагрузки: версия, область, момент и идентификатор.
    /// </summary>
    private const int PART_COUNT = 4;

    /// <summary>
    /// Кодирует курсор в непрозрачную строку.
    /// </summary>
    public string Encode()
        => CursorCodec.Encode(
            CursorCodec.VERSION,
            SCOPE,
            CursorCodec.Format(OccurredAt),
            Id.ToString(CursorCodec.GUID_FORMAT));

    /// <summary>
    /// Декодирует курсор. Битый или чужой курсор — <c>Error.Validation</c> (400).
    /// </summary>
    /// <param name="cursor">Строка курсора из query string.</param>
    public static Result<AdminAuditCursor, Failure> Decode(string cursor)
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
            || !CursorCodec.TryParseTimestamp(parts[2], out var occurredAt)
            || !Guid.TryParseExact(parts[3], CursorCodec.GUID_FORMAT, out var id)
            || id == Guid.Empty)
        {
            return Invalid();
        }

        return new AdminAuditCursor(occurredAt.ToUniversalTime(), id);
    }

    private static Failure Invalid()
        => new(Error.Validation(
            INVALID_CURSOR_CODE,
            Messages.CURSOR_INVALID,
            "cursor"));
}
