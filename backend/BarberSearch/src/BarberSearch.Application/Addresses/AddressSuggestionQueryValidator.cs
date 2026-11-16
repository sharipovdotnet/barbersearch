using System.Text;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Addresses;

/// <summary>
/// Нормализованный запрос подсказок: единственный вид, который видят обработчики и порт.
/// </summary>
/// <remarks>
/// Создаётся только <see cref="AddressSuggestionQueryValidator.Prepare"/>: поля уже обрезаны,
/// пробелы схлопнуты, пустые необязательные значения заменены на <c>null</c>, лимит зажат
/// в допустимый диапазон.
/// </remarks>
/// <param name="Query">Поисковая строка без пробелов по краям и с одиночными пробелами внутри.</param>
/// <param name="CityProviderId">Идентификатор выбранного города либо <c>null</c>.</param>
/// <param name="City">Свободный текст города либо <c>null</c>.</param>
/// <param name="Limit">Сколько подсказок запросить: от <c>1</c> до максимума из настроек.</param>
internal sealed record PreparedQuery(
    string Query,
    string? CityProviderId,
    string? City,
    int Limit);

/// <summary>
/// Общая валидация запросов подсказок города и адреса.
/// </summary>
/// <remarks>
/// <para>
/// Один источник правил для обоих обработчиков: иначе города и адрес разъехались бы по
/// минимальной длине, схлопыванию пробелов и зажиму лимита, и клиент получал бы разные
/// ответы на одинаковые ошибки ввода.
/// </para>
/// <para>
/// Ошибки — <see cref="Error.Validation"/> с именем поля: <c>query</c>, <c>city</c>,
/// <c>cityProviderId</c>. Коды берутся из <see cref="ApiErrorCodes.AddressSuggestions"/>,
/// сообщения — по-английски. Пределы длин — контрактные (<see cref="AddressSuggestionLimits"/>),
/// а минимальная длина и максимум подсказок приходят из настроек: у города и адреса они разные.
/// </para>
/// <para>
/// Лимит не даёт ошибки: значение вне диапазона зажимается в <c>1..maxSuggestions</c>.
/// Это подсказка в форме, а не запрос отчёта — «пришли побольше» не повод отказывать.
/// </para>
/// </remarks>
internal static class AddressSuggestionQueryValidator
{
    /// <summary>
    /// Проверяет и нормализует запрос подсказок.
    /// </summary>
    /// <param name="query">Сырая поисковая строка из HTTP-запроса; может быть <c>null</c>.</param>
    /// <param name="minQueryLength">Минимальная длина запроса из настроек фичи.</param>
    /// <param name="maxSuggestions">Максимум подсказок из настроек фичи.</param>
    /// <param name="cityProviderId">
    /// Необязательный идентификатор выбранного города; пустой после <c>trim</c> становится
    /// <c>null</c>.
    /// </param>
    /// <param name="city">
    /// Необязательный свободный текст города; пустой после <c>trim</c> становится <c>null</c>.
    /// </param>
    /// <param name="limit">
    /// Необязательный лимит из запроса; <c>null</c> — взять <paramref name="maxSuggestions"/>,
    /// иначе зажать в <c>1..maxSuggestions</c>.
    /// </param>
    /// <returns>
    /// Успех — нормализованный <see cref="PreparedQuery"/>. Ошибка — первая нарушенная
    /// проверка в порядке: запрос, город, идентификатор города.
    /// </returns>
    public static Result<PreparedQuery, Failure> Prepare(
        string? query,
        int minQueryLength,
        int maxSuggestions,
        string? cityProviderId = null,
        string? city = null,
        int? limit = null)
    {
        var normalizedQuery = CollapseWhitespace(query);

        if (normalizedQuery.Length < minQueryLength)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AddressSuggestions.QueryTooShort,
                $"A query must contain at least {minQueryLength} characters.",
                "query"));
        }

        if (normalizedQuery.Length > AddressSuggestionLimits.MAX_QUERY_LENGTH)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AddressSuggestions.QueryTooLong,
                $"A query cannot be longer than {AddressSuggestionLimits.MAX_QUERY_LENGTH} characters.",
                "query"));
        }

        string? normalizedCity = null;
        if (city is not null)
        {
            var collapsedCity = CollapseWhitespace(city);
            if (collapsedCity.Length > AddressSuggestionLimits.MAX_CITY_LENGTH)
            {
                return new Failure(Error.Validation(
                    ApiErrorCodes.AddressSuggestions.CityTooLong,
                    $"A city cannot be longer than {AddressSuggestionLimits.MAX_CITY_LENGTH} characters.",
                    "city"));
            }

            normalizedCity = collapsedCity.Length == 0 ? null : collapsedCity;
        }

        string? normalizedCityProviderId = null;
        if (cityProviderId is not null)
        {
            var trimmedCityProviderId = cityProviderId.Trim();
            if (trimmedCityProviderId.Length > AddressSuggestionLimits.MAX_CITY_PROVIDER_ID_LENGTH
                || trimmedCityProviderId.Any(symbol => char.IsWhiteSpace(symbol)))
            {
                return new Failure(Error.Validation(
                    ApiErrorCodes.AddressSuggestions.CityProviderIdInvalid,
                    $"A city provider id cannot be longer than {AddressSuggestionLimits.MAX_CITY_PROVIDER_ID_LENGTH} characters and cannot contain whitespace.",
                    "cityProviderId"));
            }

            normalizedCityProviderId = trimmedCityProviderId.Length == 0 ? null : trimmedCityProviderId;
        }

        var effectiveLimit = limit.HasValue
            ? Math.Clamp(limit.Value, 1, maxSuggestions)
            : maxSuggestions;

        return Result.Success<PreparedQuery, Failure>(new PreparedQuery(
            normalizedQuery,
            normalizedCityProviderId,
            normalizedCity,
            effectiveLimit));
    }

    /// <summary>
    /// Обрезает пробелы по краям и схлопывает внутренние серии пробелов в один.
    /// </summary>
    /// <remarks>
    /// Пробелами считаются любые символы <c>char.IsWhiteSpace</c>, а не только <c>' '</c>:
    /// вставленный из буфера обмена адрес может содержать табуляцию, и она не должна
    /// превращаться в отдельный символ запроса. Пустая строка и строка из пробелов дают
    /// <see cref="string.Empty"/>.
    /// </remarks>
    private static string CollapseWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var symbol in value)
        {
            if (char.IsWhiteSpace(symbol))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(symbol);
        }

        return builder.ToString();
    }
}
