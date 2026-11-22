using System.Collections.ObjectModel;
using BarberSearch.Contracts.Addresses.SuggestAddresses;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Addresses;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Подсказки адреса для формы создания салона: строка списка, дебаунс и тихий отказ.
/// </summary>
/// <remarks>
/// <para>
/// Повторяет поведение веб-комбобокса адреса
/// (<c>frontend/src/components/suggestion-combobox.tsx</c>, <c>lib/address-suggestions.ts</c>):
/// запрос уходит только после паузы в наборе, не короче трёх символов, не больше восьми
/// подсказок, а выбранная строка подставляет улицу с домом без города. Разница — только в
/// подаче: у MAUI нет нативного выпадающего комбобокса, поэтому список рисуется строками
/// прямо под полем.
/// </para>
/// <para>
/// Подсказки — прогрессивное улучшение. Отказ сервера (нет ключа, лимит, <c>401</c>, <c>5xx</c>)
/// и обрыв сети закрывают список молча: поле остаётся обычным вводом, и адрес можно набрать
/// руками. Поэтому здесь нет текста ошибки — только «ничего не найдено» для пустого ответа.
/// </para>
/// <para>
/// Класс не знает про <see cref="Barber.BarberSalonCreateViewModel"/>: экран сам решает, что
/// делать с выбранной подсказкой. <c>QueryAsync</c> вызывается на каждый символ и отменяет
/// предыдущий запрос, а устаревший ответ не попадает в список — иначе медленный ответ на
/// «Лен» перезаписал бы быстрый на «Ленина».
/// </para>
/// <para>
/// <c>providerId</c> подсказки никуда не сохраняется: он живёт только в ответе и не входит
/// в тело создания салона.
/// </para>
/// </remarks>
public sealed partial class AddressSuggestions : ObservableObject
{
    /// <summary>
    /// Минимум символов запроса: короче сервер отвечает <c>400</c> и в сеть ходить незачем.
    /// </summary>
    /// <remarks>
    /// Три, а не два, как у города: улица из двух букв слишком неоднозначна, и это же
    /// значение у веба (<c>ADDRESS_SUGGESTIONS_MIN_LENGTH</c>).
    /// </remarks>
    public const int MinimumQueryLength = 3;

    /// <summary>
    /// Пауза между последним нажатием и запросом: три нажатия дают один запрос, а не три.
    /// </summary>
    /// <remarks>
    /// 250 мс — значение веба (<c>SUGGESTIONS_DEBOUNCE_MS</c>): быстрее человек не набирает,
    /// а задержка не ощущается. Пока пауза идёт, предыдущий запрос уже отменён.
    /// </remarks>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    private readonly IAddressesApi _api;

    /// <summary>
    /// Пауза до запроса; подменяется тестом, чтобы не ждать реальные 250 мс.
    /// </summary>
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>
    /// Запрос, который сейчас ждёт паузу или ответ: новый символ его отменяет.
    /// </summary>
    private CancellationTokenSource? _pending;

    /// <summary>
    /// Улица, только что выбранная из списка: повторный запрос для неё не нужен и снова
    /// открыл бы список сразу после выбора.
    /// </summary>
    private string? _selectedAddress;

    /// <summary>
    /// Создаёт подсказки поверх авторизованного клиента адресов.
    /// </summary>
    /// <param name="api">Чтение подсказок адреса.</param>
    public AddressSuggestions(IAddressesApi api)
        : this(api, static (delay, cancellationToken) => Task.Delay(delay, cancellationToken))
    {
    }

    /// <summary>
    /// Создаёт подсказки с подменяемой паузой — для тестов без ожидания таймера.
    /// </summary>
    /// <param name="api">Чтение подсказок адреса.</param>
    /// <param name="delay">Пауза перед запросом.</param>
    internal AddressSuggestions(
        IAddressesApi api,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(delay);

        _api = api;
        _delay = delay;
    }

    /// <summary>
    /// Подсказки последнего завершённого запроса в порядке сервера.
    /// </summary>
    public ObservableCollection<AddressSuggestionResponse> Items { get; } = [];

    /// <summary>
    /// Список открыт: есть что показать или показать «Ничего не найдено.».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private bool _isOpen;

    /// <summary>
    /// Сервер ответил пустым списком: под полем видно «Ничего не найдено.».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private bool _isEmpty;

    /// <summary>
    /// Виден ли блок подсказок под полем адреса.
    /// </summary>
    /// <remarks>
    /// Открытый список без строк и без пустого ответа не рисуется: пока идёт первый запрос,
    /// под полем ничего не появляется, и вёрстка не прыгает.
    /// </remarks>
    public bool IsVisible => IsOpen && (Items.Count > 0 || IsEmpty);

    /// <summary>
    /// Строка адреса изменилась: через паузу запрашивает подсказки.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Метод намеренно возвращает задачу: экран вызывает его без ожидания, а тест ждёт
    /// результат. Исключений наружу не бросает — ожидаемые отказы закрывают список.
    /// </para>
    /// <para>
    /// Пустой город допустим: сервер сам решает, искать ли по всей стране, но экран формы
    /// адрес до выбора города не включает вовсе.
    /// </para>
    /// </remarks>
    /// <param name="query">Текущий текст поля адреса.</param>
    /// <param name="city">Свободный текст города; <c>null</c> — без фильтра.</param>
    /// <param name="cityProviderId">Провайдерский токен выбранного города; важнее текста.</param>
    /// <returns>Завершение обработки строки: запрос, его отмена или закрытие списка.</returns>
    public Task QueryAsync(string query, string? city, string? cityProviderId)
    {
        ArgumentNullException.ThrowIfNull(query);

        var trimmed = query.Trim();

        // Очищенное поле — это и есть сброс выбора: набранный заново тот же адрес снова
        // получит подсказки, а не молчание из-за прошлого выбора.
        if (trimmed.Length < MinimumQueryLength)
        {
            _selectedAddress = null;
            Close();

            return Task.CompletedTask;
        }

        if (string.Equals(trimmed, _selectedAddress, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        // Строка отличается от выбранной — выбор снят правкой.
        _selectedAddress = null;

        var request = new CancellationTokenSource();
        var previous = _pending;
        _pending = request;

        previous?.Cancel();
        previous?.Dispose();

        return LoadAsync(trimmed, city, cityProviderId, request);
    }

    /// <summary>
    /// Запоминает выбранный адрес и закрывает список.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Значение считается той же функцией, что и подстановка в форму: улица с домом без
    /// города, а при пустой улице — <see cref="AddressSuggestionResponse.Display"/>. Иначе
    /// <see cref="QueryAsync"/> для только что подставленного текста снова открыл бы список.
    /// </para>
    /// <para>
    /// Вызывается до подстановки адреса в поле.
    /// </para>
    /// </remarks>
    /// <param name="suggestion">Выбранная подсказка.</param>
    public void Select(AddressSuggestionResponse suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        _selectedAddress = SuggestedValue(suggestion);
        Close();
    }

    /// <summary>
    /// Значение поля адреса, которое даёт выбранная подсказка.
    /// </summary>
    /// <remarks>
    /// Экран подставляет ровно это значение — и <see cref="Select"/> запоминает его же,
    /// поэтому «что записали» и «что запомнили» не могут разойтись.
    /// </remarks>
    /// <param name="suggestion">Подсказка адреса.</param>
    /// <returns>Улица с домом без города либо строка подсказки.</returns>
    internal static string SuggestedValue(AddressSuggestionResponse suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        var address = suggestion.Address?.Trim() ?? string.Empty;

        return address.Length == 0 ? suggestion.Display : address;
    }

    /// <summary>
    /// Закрывает список и отменяет незавершённый запрос.
    /// </summary>
    public void Close()
    {
        var pending = _pending;
        _pending = null;

        pending?.Cancel();
        pending?.Dispose();

        Items.Clear();
        IsOpen = false;
        IsEmpty = false;
    }

    /// <summary>
    /// Ждёт паузу, читает подсказки и показывает их, если запрос всё ещё нужен.
    /// </summary>
    /// <param name="query">Очищенная строка запроса.</param>
    /// <param name="city">Свободный текст города.</param>
    /// <param name="cityProviderId">Провайдерский токен города.</param>
    /// <param name="request">Отмена этого запроса новым символом.</param>
    private async Task LoadAsync(
        string query,
        string? city,
        string? cityProviderId,
        CancellationTokenSource request)
    {
        // Токен снимается до первого await: новый символ может уничтожить источник, пока
        // запрос ждёт паузу или ответ, а сам токен после этого остаётся пригодным.
        var cancellationToken = request.Token;

        try
        {
            await _delay(Debounce, cancellationToken).ConfigureAwait(true);

            var items = await _api
                .GetAddressSuggestionsAsync(query, city, cityProviderId, cancellationToken)
                .ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            Items.Clear();

            foreach (var item in items)
            {
                Items.Add(item);
            }

            IsEmpty = items.Count == 0;
            IsOpen = true;
        }
        catch (OperationCanceledException)
        {
            // Новый символ отменил запрос: ответ устарел, следующий запрос обновит список.
        }
        catch (BarberSearchApiException)
        {
            // Подсказки — улучшение ввода: 400/401/429/5xx просто прячут список.
            Discard(cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Нет связи: поле остаётся обычным вводом, ошибку показывать нечего.
            Discard(cancellationToken);
        }
    }

    /// <summary>
    /// Прячет список из-за отказа, но не трогает более новый запрос.
    /// </summary>
    /// <param name="cancellationToken">Токен отказавшего запроса.</param>
    private void Discard(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        Close();
    }
}
