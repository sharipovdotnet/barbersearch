using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Addresses.SuggestAddresses;
using BarberSearch.Application.Addresses.SuggestCities;
using BarberSearch.Contracts.Addresses.SuggestAddresses;
using BarberSearch.Contracts.Addresses.SuggestCities;
using BarberSearch.Web.Common;
using BarberSearch.Web.Deployment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Подсказки города и адреса салона: прокси к внешнему гео-провайдеру.
/// </summary>
/// <remarks>
/// <para>
/// Подсказки города анонимны: фильтр «Город» стоит на публичной витрине и в мобильном
/// приложении, где поиск работает без входа, а подсказка — прогрессивное улучшение поля.
/// Подсказки адреса остаются только для аутентифицированного пользователя (клиент, барбер или
/// администратор): заполнение адреса идёт из формы салона, а она доступна лишь после входа,
/// поэтому платная квота провайдера не тратится анонимно на улицы и дома.
/// </para>
/// <para>
/// Оба действия ограничены политикой <c>addresses</c> — 60 запросов в минуту на пользователя
/// (claim <c>sub</c>, без него — на адрес клиента). Лишний запрос получает <c>429</c>
/// с <c>Retry-After</c> и кодом <c>rateLimit.exceeded</c>.
/// </para>
/// <para>
/// Ошибки <c>400</c> общие для обоих действий — валидация с
/// <c>invalidField</c> (<c>addressSuggestions.query.tooShort</c>,
/// <c>addressSuggestions.query.tooLong</c>, <c>addressSuggestions.city.tooLong</c>,
/// <c>addressSuggestions.cityProviderId</c>); <c>409 addressSuggestions.unavailable</c> —
/// подсказки выключены на сервере, ключ не настроен, провайдер ответил ошибкой или не ответил
/// вовремя. На <c>409</c> форма остаётся набором обычных текстовых полей: подсказки —
/// прогрессивное улучшение, а не новое обязательное поле.
/// </para>
/// <para>
/// Ключ провайдера не покидает сервер: клиент не знает ни вендора, ни ключа, а запросы идут
/// через бэкенд. В логах нет ни запроса, ни адреса, ни тела ответа провайдера. Оба действия
/// только читают: в БД и журнал аудита они ничего не пишут и домен не меняют.
/// </para>
/// <para>
/// <c>providerId</c> из ответа городов — непрозрачный токен выбранного города (у DaData это
/// <c>fias_id</c>). Его нужно передать в <c>cityProviderId</c> запроса адреса: только так
/// подсказки улиц приходят из нужного города, а не из всей страны. Никуда не сохраняется,
/// поэтому формат токена — деталь провайдера, и клиент его не разбирает.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[EnableRateLimiting(RateLimitSettings.AddressesPolicyName)]
[Route("api/addresses")]
public sealed class AddressesController : ApiController
{
    /// <summary>
    /// Подсказывает города по началу названия.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Публичное действие: аноним допускается, потому что город подсказывают фильтру витрины
    /// и мобильного поиска, доступного до входа. Лимит <c>addresses</c> — 60 запросов в минуту
    /// на пользователя, а без входа — на адрес клиента. Ответ — голый JSON-массив
    /// <c>CitySuggestionResponse</c>; если совпадений нет, приходит <c>200 []</c>, а не ошибка.
    /// </para>
    /// <para>
    /// <paramref name="query"/> тримится. Короче <c>AddressSuggestions:MinCityQueryLength</c>
    /// (по умолчанию 2 символа) — <c>400 addressSuggestions.query.tooShort</c>: порог 3 здесь
    /// не годится, город может называться «Аша» или «Уфа». Длиннее 120 символов —
    /// <c>400 addressSuggestions.query.tooLong</c>. <paramref name="limit"/> вне диапазона
    /// <c>400</c> не даёт: значение зажимается в
    /// <c>1..AddressSuggestions:MaxSuggestions</c> (по умолчанию 8).
    /// </para>
    /// <para>
    /// <c>409 addressSuggestions.unavailable</c> — подсказки выключены, ключ не настроен или
    /// провайдер недоступен; <c>429</c> — превышен лимит.
    /// </para>
    /// </remarks>
    /// <param name="query">Начало названия города; тримится, минимум — 2 символа.</param>
    /// <param name="limit">Сколько подсказок вернуть; по умолчанию и максимум — 8.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [AllowAnonymous]
    [HttpGet("cities")]
    public async Task<ActionResult<IReadOnlyList<CitySuggestionResponse>>> Cities(
        [FromQuery] string? query,
        [FromQuery] int? limit,
        [FromServices] IQueryHandler<IReadOnlyList<CitySuggestionResponse>, SuggestCitiesQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new SuggestCitiesQuery(query, limit),
            cancellationToken));

    /// <summary>
    /// Подсказывает улицы и дома внутри выбранного города.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Аутентифицированное действие с лимитом <c>addresses</c> 60 запросов в минуту на
    /// пользователя. Ответ — массив <c>AddressSuggestionResponse</c>; пустой результат —
    /// <c>200 []</c>. Поля ответа заполняют форму: <c>display</c> — единственная строка,
    /// которую видит человек; <c>address</c> — улица с домом без города (город приходит
    /// отдельно и в <c>Salon.Address</c> не попадает); <c>postalCode</c>, <c>latitude</c>,
    /// <c>longitude</c> — из подсказки. <c>timeZoneId</c> в этом срезе всегда <c>null</c>:
    /// поле зарезервировано под следующий срез, а пока пояс выбирается в форме вручную.
    /// </para>
    /// <para>
    /// <paramref name="query"/> обязателен и тримится: короче
    /// <c>AddressSuggestions:MinQueryLength</c> (по умолчанию 3 символа) —
    /// <c>400 addressSuggestions.query.tooShort</c>, длиннее 120 символов —
    /// <c>400 addressSuggestions.query.tooLong</c>.
    /// <paramref name="cityProviderId"/> — это <c>providerId</c> из ответа
    /// <c>GET api/addresses/cities</c>: необязательный, тримится, 1..64 символа без пробелов,
    /// некорректный даёт <c>400 addressSuggestions.cityProviderId</c>.
    /// <paramref name="city"/> — фолбэк, если город ввели руками и подсказку не выбирали:
    /// тримится, длиннее 100 символов — <c>400 addressSuggestions.city.tooLong</c>.
    /// Приоритет: <paramref name="cityProviderId"/> (точный фильтр провайдера), иначе
    /// непустой <paramref name="city"/> (фильтр по строке), иначе запрос без фильтра по городу.
    /// <paramref name="limit"/> зажимается в <c>1..AddressSuggestions:MaxSuggestions</c>
    /// (по умолчанию 8) и <c>400</c> не даёт.
    /// </para>
    /// <para>
    /// <c>409 addressSuggestions.unavailable</c> — подсказки выключены, ключ не настроен или
    /// провайдер недоступен; <c>429</c> — превышен лимит. При <c>409</c> клиент не показывает
    /// подсказки и оставляет обычный текстовый ввод.
    /// </para>
    /// </remarks>
    /// <param name="query">Начало адреса; тримится, минимум — 3 символа.</param>
    /// <param name="cityProviderId">
    /// <c>providerId</c> выбранного города из ответа <c>GET api/addresses/cities</c>.
    /// </param>
    /// <param name="city">Город, введённый руками; используется, когда <paramref name="cityProviderId"/> нет.</param>
    /// <param name="limit">Сколько подсказок вернуть; по умолчанию и максимум — 8.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet("suggest")]
    public async Task<ActionResult<IReadOnlyList<AddressSuggestionResponse>>> Suggest(
        [FromQuery] string? query,
        [FromQuery] string? cityProviderId,
        [FromQuery] string? city,
        [FromQuery] int? limit,
        [FromServices] IQueryHandler<IReadOnlyList<AddressSuggestionResponse>, SuggestAddressesQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new SuggestAddressesQuery(query, cityProviderId, city, limit),
            cancellationToken));
}
