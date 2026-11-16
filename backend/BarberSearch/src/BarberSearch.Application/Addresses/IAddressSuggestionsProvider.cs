using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Addresses;

/// <summary>
/// Порт внешнего провайдера подсказок города и адреса.
/// </summary>
/// <remarks>
/// <para>
/// Порт не знает ни про HTTP, ни про вендора: реализация живёт в
/// <c>BarberSearch.Infrastructure.Geocoding</c> (сейчас — DaData, за кэширующим декоратором).
/// Application не может ссылаться на Infrastructure, поэтому контракт объявлен здесь, а
/// композиционный корень Web подставляет адаптер.
/// </para>
/// <para>
/// Ошибка порта — всегда «провайдер недоступен»: сеть, таймаут, <c>4xx/5xx</c>, битый ответ
/// или незаполненная конфигурация. Обработчики превращают её в
/// <c>409 addressSuggestions.unavailable</c> с общим английским текстом: детали вендора,
/// запрос и адрес наружу и в журнал не попадают.
/// </para>
/// <para>
/// Пустой список — валидный успех: «в этом городе такой улицы нет» — это ответ, а не сбой,
/// и повторять запрос без фильтра нельзя (вернулись бы улицы чужих городов).
/// </para>
/// </remarks>
public interface IAddressSuggestionsProvider
{
    /// <summary>
    /// Запрашивает подсказки у провайдера.
    /// </summary>
    /// <param name="request">Нормализованный запрос: что искать, границы и лимит.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>
    /// Успех — список подсказок (возможно пустой). Ошибка — провайдер недоступен или
    /// не настроен.
    /// </returns>
    Task<Result<IReadOnlyList<AddressSuggestion>, Failure>> SuggestAsync(
        AddressSuggestionRequest request,
        CancellationToken cancellationToken);
}
