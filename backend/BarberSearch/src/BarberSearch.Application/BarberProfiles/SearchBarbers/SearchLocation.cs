using System.Globalization;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.BarberProfiles.SearchBarbers;

/// <summary>
/// Геопозиция клиента для сортировки выдачи «сначала ближайшие».
/// </summary>
/// <remarks>
/// <para>
/// Пара координат необязательна: без неё поиск работает в прежнем порядке рейтинга. Если же
/// клиент передал хотя бы одну половину пары, вторая обязательна — иначе непонятно, что
/// сортировать, и молчаливый откат к рейтингу скрыл бы опечатку в запросе.
/// </para>
/// <para>
/// Валидация возвращает <c>Failure</c>, а не бросает исключение: координаты приходят из
/// query string и являются ошибкой пользователя (<c>400</c>), а не сбоем сервера.
/// </para>
/// </remarks>
/// <param name="Latitude">Широта клиента в градусах, <c>−90..90</c>.</param>
/// <param name="Longitude">Долгота клиента в градусах, <c>−180..180</c>.</param>
public sealed record SearchLocation(double Latitude, double Longitude)
{
    /// <summary>
    /// Сколько километров в одном градусе широты для равнопромежуточной аппроксимации.
    /// </summary>
    /// <remarks>
    /// Один градус меридиана — около 111.32 км; долгота масштабируется на <c>cos(широты)</c>,
    /// потому что параллели к полюсам короче. Для порядка «рядом» на городских расстояниях
    /// этого достаточно (см. комментарий в репозитории).
    /// </remarks>
    public const double KILOMETRES_PER_DEGREE = 111.32;

    /// <summary>
    /// Проверяет и создаёт геопозицию из необязательной пары query-параметров.
    /// </summary>
    /// <remarks>
    /// Обе координаты отсутствуют — успех с <c>null</c>: гео-режим не запрошен.
    /// Одна отсутствует, значение не конечно или вне диапазона — <c>Failure</c> с кодом
    /// <c>barberSearch.location.invalid</c> и полем того параметра, который нарушил правило.
    /// </remarks>
    /// <param name="latitude">Широта из query string либо <c>null</c>.</param>
    /// <param name="longitude">Долгота из query string либо <c>null</c>.</param>
    public static Result<SearchLocation?, Failure> TryCreate(double? latitude, double? longitude)
    {
        if (latitude is null && longitude is null)
        {
            return Result.Success<SearchLocation?, Failure>(null);
        }

        if (latitude is null)
        {
            return Invalid(
                "Latitude is required when longitude is provided.",
                "latitude");
        }

        if (longitude is null)
        {
            return Invalid(
                "Longitude is required when latitude is provided.",
                "longitude");
        }

        if (!IsValidLatitude(latitude.Value))
        {
            return Invalid(
                $"""
                 Latitude must be a finite number between {Format(Constants.MIN_LATITUDE)} and
                 {Format(Constants.MAX_LATITUDE)}, got {Format(latitude.Value)}.
                 """,
                "latitude");
        }

        if (!IsValidLongitude(longitude.Value))
        {
            return Invalid(
                $"""
                 Longitude must be a finite number between {Format(Constants.MIN_LONGITUDE)} and
                 {Format(Constants.MAX_LONGITUDE)}, got {Format(longitude.Value)}.
                 """,
                "longitude");
        }

        return new SearchLocation(latitude.Value, longitude.Value);
    }

    /// <summary>
    /// Широта конечна и лежит в допустимом диапазоне.
    /// </summary>
    /// <remarks>
    /// <c>double.IsFinite</c> отсекает <c>NaN</c> и бесконечности: сравнения с ними ложны,
    /// поэтому без отдельной проверки <c>NaN</c> прошёл бы мимо диапазона и дал бы пустой
    /// <c>ORDER BY</c> вместо честного <c>400</c>.
    /// </remarks>
    private static bool IsValidLatitude(double latitude)
        => double.IsFinite(latitude)
           && latitude is >= Constants.MIN_LATITUDE and <= Constants.MAX_LATITUDE;

    /// <summary>
    /// Долгота конечна и лежит в допустимом диапазоне.
    /// </summary>
    private static bool IsValidLongitude(double longitude)
        => double.IsFinite(longitude)
           && longitude is >= Constants.MIN_LONGITUDE and <= Constants.MAX_LONGITUDE;

    /// <summary>
    /// Форматирует число в invariant culture: сообщение об ошибке не должно зависеть
    /// от культуры сервера (иначе <c>55.5</c> стало бы <c>55,5</c> и сбивало бы клиента).
    /// </summary>
    private static string Format(double value)
        => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Ошибка валидации геопозиции с указанием конкретного поля.
    /// </summary>
    private static Failure Invalid(string detail, string fieldName)
        => new(Error.Validation(
            ApiErrorCodes.Common.BarberSearchLocationInvalid,
            detail,
            fieldName));
}
