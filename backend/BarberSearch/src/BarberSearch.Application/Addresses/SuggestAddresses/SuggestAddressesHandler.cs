using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Addresses.SuggestAddresses;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Addresses.SuggestAddresses;

/// <summary>
/// Обработчик подсказок адреса внутри выбранного города.
/// </summary>
/// <remarks>
/// <para>
/// Сначала город, потом адрес: <c>cityProviderId</c> из выбора города даёт провайдеру точный
/// фильтр, поэтому улицы приходят из нужного города, а не из всей страны. Если человек ввёл
/// город руками, уходит свободный <c>city</c>; если не задано ни то ни другое — запрос без
/// фильтра. Приоритет выполняет адаптер, обработчик передаёт оба поля как есть.
/// </para>
/// <para>
/// Если фича не настроена, порт <b>не вызывается</b>: выключенная фича не должна зависеть от
/// того, какой адаптер зарегистрирован, и отвечает <c>409 addressSuggestions.unavailable</c>.
/// Та же ошибка возвращается на любой сбой порта: текст общий, без деталей вендора и без
/// строки запроса, чтобы не отдавать наружу адрес, который человек вводил.
/// </para>
/// <para>
/// <c>timeZoneId</c> в ответе всегда <c>null</c>: в этом срезе пояс по-прежнему выбирается
/// в форме вручную. Поле зарезервировано в контракте сразу, чтобы следующий срез
/// (офлайн <c>lat</c>/<c>lon</c> → IANA) не менял форму ответа, и заполняться будет здесь же.
/// </para>
/// <para>
/// Пустой список — успех (<c>200 []</c>), а не ошибка: пустой ответ с выбранным городом
/// означает «в этом городе такой улицы нет», и повторять запрос без фильтра нельзя — вернулись
/// бы улицы чужих городов.
/// </para>
/// </remarks>
public sealed class SuggestAddressesHandler
    : IQueryHandler<IReadOnlyList<AddressSuggestionResponse>, SuggestAddressesQuery>
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

    public SuggestAddressesHandler(
        IAddressSuggestionsProvider provider,
        AddressSuggestionsOptions options)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(options);

        _provider = provider;
        _options = options;
    }

    /// <summary>
    /// Возвращает подсказки адреса; короткий запрос или некорректный фильтр города — <c>400</c>,
    /// выключенная фича или сбой провайдера — <c>409 addressSuggestions.unavailable</c>.
    /// </summary>
    public async Task<Result<IReadOnlyList<AddressSuggestionResponse>, Failure>> Handle(
        SuggestAddressesQuery query,
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
            _options.MinQueryLength,
            _options.MaxSuggestions,
            cityProviderId: query.CityProviderId,
            city: query.City,
            limit: query.Limit);
        if (prepared.IsFailure)
        {
            return prepared.Error;
        }

        var request = new AddressSuggestionRequest(
            prepared.Value.Query,
            AddressSuggestionBound.Address,
            prepared.Value.CityProviderId,
            prepared.Value.City,
            prepared.Value.Limit);

        var suggestions = await _provider.SuggestAsync(request, cancellationToken);
        if (suggestions.IsFailure)
        {
            return new Failure(Error.Conflict(
                ApiErrorCodes.AddressSuggestions.Unavailable,
                UNAVAILABLE_MESSAGE));
        }

        return suggestions.Value
            .Select(suggestion => new AddressSuggestionResponse(
                suggestion.ProviderId,
                suggestion.Display,
                suggestion.Address,
                suggestion.City,
                suggestion.Region,
                suggestion.Country,
                suggestion.PostalCode,
                suggestion.Latitude,
                suggestion.Longitude,
                TimeZoneId: null))
            .ToList();
    }
}
