using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Addresses.SuggestCities;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Addresses.SuggestCities;

/// <summary>
/// Обработчик подсказок города для формы салона.
/// </summary>
/// <remarks>
/// <para>
/// Города тоже приходят от провайдера, а не из нашей БД: <c>SELECT DISTINCT city</c> по салонам
/// не даёт <c>fias</c>, без которого нельзя точно отфильтровать улицы, и не знает городов,
/// где салонов ещё нет. Выбор подсказки заполняет поле «Город» и запоминает
/// <c>providerId</c> в состоянии формы — в домен он не сохраняется.
/// </para>
/// <para>
/// Если фича не настроена, порт <b>не вызывается</b>: выключенная фича не должна зависеть от
/// того, какой адаптер зарегистрирован (в тестах и в бою он разный), и отвечает
/// <c>409 addressSuggestions.unavailable</c>. Та же ошибка возвращается на любой сбой порта:
/// текст общий, без деталей вендора и без строки запроса, чтобы не отдавать наружу адрес,
/// который человек вводил.
/// </para>
/// <para>
/// Пустой список — успех (<c>200 []</c>), а не ошибка: «города с таким названием нет» —
/// это ответ, и подменять его на <c>200</c> с чужими строками нельзя.
/// </para>
/// </remarks>
public sealed class SuggestCitiesHandler
    : IQueryHandler<IReadOnlyList<CitySuggestionResponse>, SuggestCitiesQuery>
{
    /// <summary>
    /// Текст ошибки «фича выключена или не настроена».
    /// </summary>
    private const string NOT_CONFIGURED_MESSAGE =
        "Address suggestions are not configured on the server.";

    /// <summary>
    /// Текст ошибки «провайдер недоступен»: общий, без деталей вендора и запроса.
    /// </summary>
    private const string UNAVAILABLE_MESSAGE =
        "Address suggestions are temporarily unavailable.";

    private readonly IAddressSuggestionsProvider _provider;
    private readonly AddressSuggestionsOptions _options;

    public SuggestCitiesHandler(
        IAddressSuggestionsProvider provider,
        AddressSuggestionsOptions options)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(options);

        _provider = provider;
        _options = options;
    }

    /// <summary>
    /// Возвращает подсказки города; короткий запрос — <c>400</c>, выключенная фича или сбой
    /// провайдера — <c>409 addressSuggestions.unavailable</c>.
    /// </summary>
    public async Task<Result<IReadOnlyList<CitySuggestionResponse>, Failure>> Handle(
        SuggestCitiesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!_options.IsConfigured)
        {
            return new Failure(Error.Conflict(
                ApiErrorCodes.AddressSuggestions.Unavailable,
                NOT_CONFIGURED_MESSAGE));
        }

        var prepared = AddressSuggestionQueryValidator.Prepare(
            query.Query,
            _options.MinCityQueryLength,
            _options.MaxSuggestions,
            limit: query.Limit);
        if (prepared.IsFailure)
        {
            return prepared.Error;
        }

        var request = new AddressSuggestionRequest(
            prepared.Value.Query,
            AddressSuggestionBound.City,
            CityProviderId: null,
            City: null,
            prepared.Value.Limit);

        var suggestions = await _provider.SuggestAsync(request, cancellationToken);
        if (suggestions.IsFailure)
        {
            return new Failure(Error.Conflict(
                ApiErrorCodes.AddressSuggestions.Unavailable,
                UNAVAILABLE_MESSAGE));
        }

        return suggestions.Value
            .Select(suggestion => new CitySuggestionResponse(
                suggestion.ProviderId,
                suggestion.Display,
                suggestion.City,
                suggestion.Region,
                suggestion.Country,
                suggestion.Latitude,
                suggestion.Longitude))
            .ToList();
    }
}
