using BarberSearch.Domain;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Common;

/// <summary>
/// Курсор keyset-пагинации поиска барберов: последняя пара <c>(rating, id)</c> страницы.
/// </summary>
/// <remarks>
/// Порядок поиска — <c>rating DESC NULLS LAST, id</c>; курсор хранит оба ключа, поэтому
/// следующая страница продолжается строго после последней строки и не зависит от OFFSET.
/// <see cref="Rating"/> = <c>null</c> — отдельная группа «без отзывов», она идёт последней.
/// </remarks>
/// <param name="Rating">Рейтинг последней строки страницы либо <c>null</c>.</param>
/// <param name="Id">Идентификатор последней строки страницы.</param>
public sealed record BarberSearchCursor(decimal? Rating, Guid Id)
{
    /// <summary>
    /// Публичный код ошибки «курсор поиска барберов не разбирается».
    /// </summary>
    private const string INVALID_CURSOR_CODE = "barberSearch.cursor.invalid";

    /// <summary>
    /// Кодирует курсор в непрозрачную строку.
    /// </summary>
    public string Encode()
        => CursorCodec.Encode(
            CursorCodec.VERSION,
            Rating is null ? string.Empty : CursorCodec.Format(Rating.Value),
            Id.ToString(CursorCodec.GUID_FORMAT));

    /// <summary>
    /// Декодирует курсор. Битый или подделанный курсор — <c>Error.Validation</c> (400).
    /// </summary>
    /// <param name="cursor">Строка курсора из query string.</param>
    public static Result<BarberSearchCursor, Failure> Decode(string cursor)
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

        if (parts[0] != CursorCodec.VERSION)
        {
            return Invalid();
        }

        decimal? rating = null;

        if (parts[1].Length > 0)
        {
            if (!CursorCodec.TryParseDecimal(parts[1], out var parsedRating)
                || parsedRating < Constants.MIN_RATING
                || parsedRating > Constants.MAX_RATING)
            {
                return Invalid();
            }

            rating = parsedRating;
        }

        if (!Guid.TryParseExact(parts[2], CursorCodec.GUID_FORMAT, out var id) || id == Guid.Empty)
        {
            return Invalid();
        }

        return new BarberSearchCursor(rating, id);
    }

    private static Failure Invalid()
        => new(Error.Validation(
            INVALID_CURSOR_CODE,
            Messages.CURSOR_INVALID,
            "cursor"));
}
