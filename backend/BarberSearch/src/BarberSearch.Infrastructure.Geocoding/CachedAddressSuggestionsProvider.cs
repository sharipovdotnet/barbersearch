using System.Collections.Concurrent;
using System.Globalization;
using BarberSearch.Application.Addresses;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Infrastructure.Geocoding;

/// <summary>
/// Декоратор порта подсказок адреса: кэширует успешные ответы провайдера.
/// </summary>
/// <remarks>
/// <para>
/// Порядок вызовов в DI: <see cref="IAddressSuggestionsProvider"/> → этот декоратор →
/// <see cref="DadataAddressSuggestionsProvider"/>. Подсказки — чтение с внешней квотой,
/// и повторный запрос той же строки (правка на один символ вперёд и назад, несколько
/// браузеров, повторная отрисовка формы) не должен стоить обращения к вендору.
/// </para>
/// <para>
/// Кэш — свой, на <see cref="ConcurrentDictionary{TKey,TValue}"/>: отдельный пакет
/// кэширования ради одной фичи не тянется. Ключ — нормализованные
/// <c>bound|query|cityProviderId|city|limit</c>: <c>trim</c>, схлопнутые пробелы,
/// <see cref="string.ToLowerInvariant"/>, отсутствующие значения — пустая строка. Город
/// входит в ключ, потому что подсказки улиц у разных городов разные, а <c>limit</c> —
/// потому что ответ провайдера зависит от него.
/// </para>
/// <para>
/// <see cref="AddressSuggestionsOptions.CacheTtlSeconds"/> <c>0</c> или меньше выключает кэш
/// целиком: запрос каждый раз идёт дальше, чтения и записи нет. Кэшируются только успехи,
/// включая пустой список (он означает «в этом городе такой улицы нет»); <see cref="Failure"/>
/// не кэшируется — недоступность провайдера не должна залипать на четверть часа.
/// </para>
/// <para>
/// Время берётся только из <see cref="TimeProvider"/>: в тестах TTL двигается без ожидания,
/// а серверные часы не читаются напрямую. Размер ограничен
/// <see cref="MAX_CACHE_ENTRIES"/>: при переполнении сначала выкидываются истёкшие записи,
/// затем самые старые по времени создания. Гонки при подрезке безвредны — худшее, что
/// случится, это лишний вызов провайдера.
/// </para>
/// </remarks>
public sealed class CachedAddressSuggestionsProvider : IAddressSuggestionsProvider
{
    /// <summary>
    /// Жёсткий предел числа записей кэша.
    /// </summary>
    /// <remarks>
    /// 2048 записей — это единицы мегабайт на самый широкий сценарий и защита от роста при
    /// переборе строк. Предел не настраивается: он про память процесса, а не про фичу.
    /// </remarks>
    public const int MAX_CACHE_ENTRIES = 2048;

    /// <summary>
    /// Разделитель частей ключа. Значения нормализованы, поэтому разделитель однозначен.
    /// </summary>
    private const char KEY_SEPARATOR = '|';

    private readonly IAddressSuggestionsProvider _inner;
    private readonly AddressSuggestionsOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public CachedAddressSuggestionsProvider(
        IAddressSuggestionsProvider inner,
        AddressSuggestionsOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _inner = inner;
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<AddressSuggestion>, Failure>> SuggestAsync(
        AddressSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var cacheTtlSeconds = _options.CacheTtlSeconds;
        if (cacheTtlSeconds <= 0)
        {
            // Кэш выключен конфигурацией: каждый запрос идёт к провайдеру, словарь не трогаем.
            return await _inner
                .SuggestAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }

        var cacheTtl = TimeSpan.FromSeconds(cacheTtlSeconds);
        var now = _timeProvider.GetUtcNow();
        var key = BuildKey(request);

        if (_cache.TryGetValue(key, out var cached) && now - cached.CreatedAt < cacheTtl)
        {
            return Result.Success<IReadOnlyList<AddressSuggestion>, Failure>(cached.Suggestions);
        }

        var result = await _inner
            .SuggestAsync(request, cancellationToken)
            .ConfigureAwait(false);

        // Отказ не кэшируется: провайдер мог быть недоступен секунду назад, а залипший
        // на TTL отказ вернул бы 409 даже после восстановления.
        if (result.IsFailure)
        {
            return result;
        }

        Store(key, result.Value, now);

        return result;
    }

    /// <summary>
    /// Собирает ключ кэша из нормализованных частей запроса.
    /// </summary>
    /// <remarks>
    /// <c>bound</c> и <c>limit</c> — числа, чтобы ключ не зависел от имени члена перечисления;
    /// строки нормализуются <see cref="Normalize"/>.
    /// </remarks>
    private static string BuildKey(AddressSuggestionRequest request)
        => string.Join(
            KEY_SEPARATOR,
            ((int)request.Bound).ToString(CultureInfo.InvariantCulture),
            Normalize(request.Query),
            Normalize(request.CityProviderId),
            Normalize(request.City),
            request.Limit.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Нормализует часть ключа: пустое и отсутствующее значение — пустая строка, внутренние
    /// пробелы схлопнуты, регистр снят.
    /// </summary>
    /// <remarks>
    /// <see cref="string.ToLowerInvariant"/> — чтобы ключ не зависел от культуры процесса;
    /// схлопывание пробелов — чтобы «ул.  Лен» и «ул. Лен» считались одной строкой.
    /// </remarks>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string
            .Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }

    /// <summary>
    /// Кладёт успешный ответ в кэш и подрезает словарь до <see cref="MAX_CACHE_ENTRIES"/>.
    /// </summary>
    private void Store(string key, IReadOnlyList<AddressSuggestion> suggestions, DateTimeOffset now)
    {
        _cache[key] = new CacheEntry(suggestions, now);

        if (_cache.Count > MAX_CACHE_ENTRIES)
        {
            Trim(now);
        }
    }

    /// <summary>
    /// Подрезает кэш: сначала истёкшие записи, затем самые старые по <c>CreatedAt</c>.
    /// </summary>
    /// <remarks>
    /// Только операции <see cref="ConcurrentDictionary{TKey,TValue}"/>, без блокировок.
    /// Гонки безвредны: параллельный поток мог уже удалить запись или перезаписать её
    /// свежим ответом — тогда удаление либо не найдёт ключ, либо снимет запись, которую
    /// следующий запрос просто пересчитает. Инвариант «словарь не больше предела» при
    /// этом восстанавливается на следующей вставке.
    /// </remarks>
    private void Trim(DateTimeOffset now)
    {
        var cacheTtl = TimeSpan.FromSeconds(_options.CacheTtlSeconds);

        foreach (var entry in _cache)
        {
            if (now - entry.Value.CreatedAt >= cacheTtl)
            {
                _ = _cache.TryRemove(entry.Key, out _);
            }
        }

        while (_cache.Count > MAX_CACHE_ENTRIES)
        {
            var oldestKey = FindOldestKey();
            if (oldestKey is null)
            {
                break;
            }

            _ = _cache.TryRemove(oldestKey, out _);
        }
    }

    /// <summary>
    /// Ищет ключ самой старой записи; <c>null</c>, если словарь пуст.
    /// </summary>
    private string? FindOldestKey()
    {
        string? oldestKey = null;
        var oldestCreatedAt = DateTimeOffset.MaxValue;

        foreach (var entry in _cache)
        {
            if (entry.Value.CreatedAt < oldestCreatedAt)
            {
                oldestCreatedAt = entry.Value.CreatedAt;
                oldestKey = entry.Key;
            }
        }

        return oldestKey;
    }

    /// <summary>
    /// Запись кэша: подсказки и момент их получения.
    /// </summary>
    /// <remarks>
    /// Ссылка на список отдаётся всем читателям кэша: <see cref="IReadOnlyList{T}"/> меняться
    /// не должен, и провайдер собирает новый список на каждый ответ.
    /// </remarks>
    private sealed record CacheEntry(
        IReadOnlyList<AddressSuggestion> Suggestions,
        DateTimeOffset CreatedAt);
}
