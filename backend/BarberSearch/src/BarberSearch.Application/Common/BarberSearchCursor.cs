using BarberSearch.Domain;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Common;

/// <summary>
/// Курсор keyset-пагинации поиска барберов: последняя строка страницы.
/// </summary>
/// <remarks>
/// <para>
/// В обычном режиме порядок — <c>rating DESC NULLS LAST, id</c>; в гео-режиме — расстояние
/// до ближайшего активного салона (<c>null</c> — последняя группа) и <c>id</c>, рейтинг в нём
/// не участвует. Курсор хранит оба ключа, поэтому следующая страница продолжается строго
/// после последней строки и не зависит от OFFSET.
/// </para>
/// <para>
/// Формат версии 2 (<c>version|rating|id|distance</c>) пишется всегда; пустая последняя часть
/// означает <c>null</c>. Курсоры версии 1 (<c>version|rating|id</c>) продолжают читаться:
/// клиенты, начавшие листать до гео-фичи, не должны получить <c>400</c> на середине выдачи.
/// </para>
/// </remarks>
/// <param name="Rating">Рейтинг последней строки страницы либо <c>null</c>.</param>
/// <param name="Id">Идентификатор последней строки страницы.</param>
/// <param name="DistanceKm">
/// Расстояние последней строки страницы в километрах либо <c>null</c>: нет гео-фильтра
/// (формат версии 1) или последняя строка без активного салона.
/// </param>
public sealed record BarberSearchCursor(decimal? Rating, Guid Id, double? DistanceKm = null)
{
    /// <summary>
    /// Публичный код ошибки «курсор поиска барберов не разбирается».
    /// </summary>
    private const string INVALID_CURSOR_CODE = "barberSearch.cursor.invalid";

    /// <summary>
    /// Кодирует курсор в непрозрачную строку формата версии 2.
    /// </summary>
    /// <remarks>
    /// Расстояние пишется пустой строкой для <c>null</c>: разделитель частей обязан остаться
    /// на месте, иначе старый декодер увидел бы курсор версии 1 без четвёртой части.
    /// </remarks>
    public string Encode()
        => CursorCodec.Encode(
            CursorCodec.VERSION_WITH_DISTANCE,
            Rating is null ? string.Empty : CursorCodec.Format(Rating.Value),
            Id.ToString(CursorCodec.GUID_FORMAT),
            DistanceKm is null ? string.Empty : CursorCodec.Format(DistanceKm.Value));

    /// <summary>
    /// Декодирует курсор. Битый или подделанный курсор — <c>Error.Validation</c> (400).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Гео-режим принимает только формат версии 2: версия 1 не несёт расстояния, то есть
    /// принадлежит рейтинговому порядку, и продолжать по ней другой порядок нельзя.
    /// </para>
    /// <para>
    /// Пустая distance в версии 2 — законная позиция: это группа «без активного салона»,
    /// которая в гео-выдаче идёт последней. Обратная проверка симметрична: в обычном режиме
    /// курсор с расстоянием отвергается, потому что он описывает чужую сортировку.
    /// </para>
    /// </remarks>
    /// <param name="cursor">Строка курсора из query string.</param>
    /// <param name="isGeoSearch">Идёт ли поиск с координатами клиента.</param>
    public static Result<BarberSearchCursor, Failure> Decode(string cursor, bool isGeoSearch = false)
    {
        var partsResult = CursorCodec.DecodeParts(cursor, INVALID_CURSOR_CODE);
        if (partsResult.IsFailure)
        {
            return partsResult.Error;
        }

        var parts = partsResult.Value;

        double? distanceKm = null;

        if (parts.Count == CursorCodec.PART_COUNT
            && parts[0] == CursorCodec.VERSION)
        {
            // Старый формат версии 1: расстояния в нём нет вовсе.
            if (isGeoSearch)
            {
                return Invalid();
            }
        }
        else if (parts.Count == CursorCodec.PART_COUNT_WITH_DISTANCE
                 && parts[0] == CursorCodec.VERSION_WITH_DISTANCE)
        {
            if (parts[3].Length > 0)
            {
                if (!CursorCodec.TryParseDouble(parts[3], out var parsedDistance)
                    || !double.IsFinite(parsedDistance)
                    || parsedDistance < 0)
                {
                    return Invalid();
                }

                distanceKm = parsedDistance;
            }
        }
        else
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

        if (!isGeoSearch && distanceKm is not null)
        {
            return Invalid();
        }

        return new BarberSearchCursor(rating, id, distanceKm);
    }

    private static Failure Invalid()
        => new(Error.Validation(
            INVALID_CURSOR_CODE,
            Messages.CURSOR_INVALID,
            "cursor"));
}
