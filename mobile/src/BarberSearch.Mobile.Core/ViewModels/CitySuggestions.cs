using System.Collections.ObjectModel;
using BarberSearch.Contracts.Addresses.SuggestCities;
using BarberSearch.Mobile.Core.Api;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Подсказки города для фильтра «Поиск»: строка списка, дебаунс и тихий отказ.
/// </summary>
/// <remarks>
/// <para>
/// Повторяет поведение веб-комбобокса городского фильтра
/// (<c>frontend/src/components/suggestion-combobox.tsx</c>): запрос уходит только после паузы
/// в наборе, не короче двух символов, не больше восьми подсказок, а выбор пункта подставляет
/// голое имя города. Разница — только в подаче: у MAUI нет нативного выпадающего комбобокса,
/// поэтому список рисуется строками прямо под полем.
/// </para>
/// <para>
/// Подсказки — прогрессивное улучшение. Отказ сервера (подсказки выключены, нет ключа, лимит,
/// 401, 5xx) и обрыв сети закрывают список молча: поле остаётся обычным вводом, и поиск
/// работает как раньше. Поэтому здесь нет текста ошибки — только «ничего не найдено» для
/// пустого ответа, как в вебе.
/// </para>
/// <para>
/// Класс не знает про <see cref="SearchViewModel"/>: экран сам решает, что делать с выбранным
/// городом. <c>QueryAsync</c> вызывается на каждый символ и отменяет предыдущий запрос, а
/// устаревший ответ не попадает в список — иначе медленный ответ на «Мо» перезаписал бы
/// быстрый на «Моск».
/// </para>
/// </remarks>
public sealed partial class CitySuggestions : ObservableObject
{
    /// <summary>
    /// Минимум символов запроса: короче сервер отвечает <c>400</c> и в сеть ходить незачем.
    /// </summary>
    /// <remarks>
    /// Два, а не три: город может называться «Аша» или «Уфа» — то же правило, что у сервера.
    /// </remarks>
    public const int MinimumQueryLength = 2;

    /// <summary>
    /// Пауза между последним нажатием и запросом: три нажатия дают один запрос, а не три.
    /// </summary>
    /// <remarks>
    /// 250 мс — значение веба (<c>SUGGESTIONS_DEBOUNCE_MS</c>): быстрее человек не набирает,
    /// а задержка не ощущается. Пока пауза идёт, предыдущий запрос уже отменён.
    /// </remarks>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    private readonly IBarberSearchApi _api;

    /// <summary>
    /// Пауза до запроса; подменяется тестом, чтобы не ждать реальные 250 мс.
    /// </summary>
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>
    /// Запрос, который сейчас ждёт паузу или ответ: новый символ его отменяет.
    /// </summary>
    private CancellationTokenSource? _pending;

    /// <summary>
    /// Город, только что выбранный из списка: повторный запрос для него не нужен и снова
    /// открыл бы список сразу после выбора.
    /// </summary>
    private string? _selectedCity;

    /// <summary>
    /// Создаёт подсказки поверх публичного API.
    /// </summary>
    /// <param name="api">Чтение подсказок города.</param>
    public CitySuggestions(IBarberSearchApi api)
        : this(api, static (delay, cancellationToken) => Task.Delay(delay, cancellationToken))
    {
    }

    /// <summary>
    /// Создаёт подсказки с подменяемой паузой — для тестов без ожидания таймера.
    /// </summary>
    /// <param name="api">Чтение подсказок города.</param>
    /// <param name="delay">Пауза перед запросом.</param>
    internal CitySuggestions(
        IBarberSearchApi api,
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
    public ObservableCollection<CitySuggestionResponse> Items { get; } = [];

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
    /// Виден ли блок подсказок под полем города.
    /// </summary>
    /// <remarks>
    /// Открытый список без строк и без пустого ответа не рисуется: пока идёт первый запрос,
    /// под полем ничего не появляется, и вёрстка не прыгает.
    /// </remarks>
    public bool IsVisible => IsOpen && (Items.Count > 0 || IsEmpty);

    /// <summary>
    /// Строка города изменилась: через паузу запрашивает подсказки.
    /// </summary>
    /// <remarks>
    /// Метод намеренно возвращает задачу: экран вызывает его без ожидания, а тест ждёт
    /// результат. Исключений наружу не бросает — ожидаемые отказы закрывают список.
    /// </remarks>
    /// <param name="query">Текущий текст поля города.</param>
    /// <returns>Завершение обработки строки: запрос, его отмена или закрытие списка.</returns>
    public Task QueryAsync(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var trimmed = query.Trim();

        // Очищенное поле — это и есть сброс выбора: набранный заново тот же город снова
        // получит подсказки, а не молчание из-за прошлого выбора.
        if (trimmed.Length < MinimumQueryLength)
        {
            _selectedCity = null;
            Close();

            return Task.CompletedTask;
        }

        if (string.Equals(trimmed, _selectedCity, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        // Строка отличается от выбранной — выбор снят правкой.
        _selectedCity = null;

        var request = new CancellationTokenSource();
        var previous = _pending;
        _pending = request;

        previous?.Cancel();
        previous?.Dispose();

        return LoadAsync(trimmed, request);
    }

    /// <summary>
    /// Запоминает выбранный город и закрывает список.
    /// </summary>
    /// <remarks>
    /// Вызывается до подстановки города в поле: тогда <see cref="QueryAsync"/> для того же
    /// текста увидит выбор и не станет запрашивать подсказки заново.
    /// </remarks>
    /// <param name="suggestion">Выбранная подсказка.</param>
    public void Select(CitySuggestionResponse suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        _selectedCity = suggestion.City;
        Close();
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
    /// <param name="request">Отмена этого запроса новым символом.</param>
    private async Task LoadAsync(string query, CancellationTokenSource request)
    {
        // Токен снимается до первого await: новый символ может уничтожить источник, пока
        // запрос ждёт паузу или ответ, а сам токен после этого остаётся пригодным.
        var cancellationToken = request.Token;

        try
        {
            await _delay(Debounce, cancellationToken).ConfigureAwait(true);

            var items = await _api.GetCitySuggestionsAsync(query, cancellationToken).ConfigureAwait(true);

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
            // Подсказки — улучшение ввода: 400/409/429/5xx просто прячут список.
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
