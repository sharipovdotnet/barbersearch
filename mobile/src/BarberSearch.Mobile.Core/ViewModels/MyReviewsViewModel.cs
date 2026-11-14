using System.Collections.ObjectModel;
using System.Net;
using BarberSearch.Contracts.Reviews.GetMyReviews;
using BarberSearch.Contracts.Reviews.UpdateReview;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Reviews;
using BarberSearch.Mobile.Core.Auth;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Экран «Мои отзывы»: один список из опубликованных и ожидающих проверки.
/// </summary>
/// <remarks>
/// <para>
/// Список читается один раз за визит страницы: <c>GET api/reviews/mine</c> с размером
/// страницы 10, курсор возвращается серверу как есть. Повторно список обновляется только
/// свайпом; прокрутка догружает следующую страницу, фонового опроса нет. Правка и удаление
/// меняют свою строку на месте — полный <c>GET</c> сдвинул бы страницы и прокрутил список.
/// </para>
/// <para>
/// У загрузок есть поколение: ответ, пришедший после обновления или завершения сессии,
/// отбрасывается, иначе запоздавшая страница вернула бы данные прошлого пользователя.
/// </para>
/// <para>
/// Удаление убирает строку; если страница опустела, а курсор есть, следующая догружается
/// сразу. <c>404</c> на удаление — не ошибка: отзыв уже удалён, в том числе с другого
/// устройства, и для списка это тот же результат.
/// </para>
/// </remarks>
public sealed partial class MyReviewsViewModel : ObservableObject
{
    /// <summary>
    /// Размер страницы — как в вебе.
    /// </summary>
    public const int PageSize = 10;

    private const string LoadErrorText = "Не удалось загрузить отзывы.";
    private const string LoadMoreErrorText = "Не удалось загрузить следующие отзывы.";
    private const string NetworkErrorText =
        "Нет связи с сервером. Проверьте подключение и повторите попытку.";
    private const string TooManyRequestsText =
        "Слишком много запросов. Подождите минуту и повторите.";
    private const string DeletedAnnouncement = "Отзыв удалён.";
    private const string AlreadyDeletedAnnouncement = "Этот отзыв уже удалён.";
    private const string UpdatedAnnouncement = "Отзыв обновлён.";
    private const string DeleteForbiddenText = "Удалить отзыв может только его автор.";
    private const string DeleteFailedText = "Не удалось удалить отзыв.";

    /// <summary>
    /// Текст пустого состояния.
    /// </summary>
    public const string EmptyTitle = "Пока нет отзывов.";

    /// <summary>
    /// Объяснение пустого состояния.
    /// </summary>
    public const string EmptyDescription =
        "Отзыв можно оставить в «Моих записях» после завершённого визита.";

    private readonly IReviewsApi _reviewsApi;
    private readonly TokenSession _session;

    private string? _nextCursor;
    private int _generation;

    /// <summary>
    /// Создаёт экран отзывов.
    /// </summary>
    /// <param name="reviewsApi">Список, правка и удаление отзывов.</param>
    /// <param name="session">Сессия — источник правды о входе.</param>
    public MyReviewsViewModel(IReviewsApi reviewsApi, TokenSession session)
    {
        ArgumentNullException.ThrowIfNull(reviewsApi);
        ArgumentNullException.ThrowIfNull(session);

        _reviewsApi = reviewsApi;
        _session = session;
    }

    /// <summary>
    /// Нажата «Изменить» у строки: страница открывает экран правки.
    /// </summary>
    public event EventHandler<MyReviewListItem>? EditRequested;

    /// <summary>
    /// Строки списка в порядке ответа сервера.
    /// </summary>
    public ObservableCollection<MyReviewListItem> Items { get; } = [];

    /// <summary>
    /// Текст пустого состояния: заголовок.
    /// </summary>
    public string EmptyStateTitle { get; } = EmptyTitle;

    /// <summary>
    /// Текст пустого состояния: объяснение.
    /// </summary>
    public string EmptyStateDescription { get; } = EmptyDescription;

    /// <summary>
    /// Пользователь вошёл.
    /// </summary>
    [ObservableProperty]
    private bool _isAuthenticated;

    /// <summary>
    /// Идёт первая загрузка списка.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isFirstLoading;

    /// <summary>
    /// Идёт обновление свайпом: индикатор держит <c>RefreshView</c>.
    /// </summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>
    /// Идёт догрузка следующей страницы.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingMore;

    /// <summary>
    /// Ошибка первой страницы; <c>null</c>, когда её нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowList))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private string? _errorMessage;

    /// <summary>
    /// Ошибка догрузки: уже загруженные строки остаются.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadMoreError))]
    private string? _loadMoreError;

    /// <summary>
    /// Первая страница уже загружалась.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _hasLoaded;

    /// <summary>
    /// Баннер о результате действия.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnnouncement))]
    private string? _announcement;

    /// <summary>
    /// Показывать индикатор первой загрузки.
    /// </summary>
    public bool ShowLoading => IsAuthenticated && IsFirstLoading;

    /// <summary>
    /// Показывать ошибку первой страницы с «Повторить».
    /// </summary>
    public bool ShowError => IsAuthenticated && !IsFirstLoading && ErrorMessage is not null;

    /// <summary>
    /// Показывать список.
    /// </summary>
    public bool ShowList => IsAuthenticated && !ShowError && !IsFirstLoading;

    /// <summary>
    /// Показывать пустое состояние: только когда загрузка закончилась и строк нет.
    /// </summary>
    public bool ShowEmptyState => IsAuthenticated && HasLoaded && !IsFirstLoading && Items.Count == 0;

    /// <summary>
    /// Есть следующая страница.
    /// </summary>
    public bool CanLoadMore => _nextCursor is not null;

    /// <summary>
    /// Показывать ошибку догрузки в футере.
    /// </summary>
    public bool ShowLoadMoreError => LoadMoreError is not null;

    /// <summary>
    /// Виден ли баннер о результате действия.
    /// </summary>
    public bool HasAnnouncement => !string.IsNullOrEmpty(Announcement);

    /// <summary>
    /// Проверяет сессию и читает первую страницу один раз за визит страницы.
    /// </summary>
    /// <remarks>
    /// Повторное появление экрана в рамках сессии ничего не перечитывает: список живёт до
    /// ухода со страницы. Обновить его можно только свайпом — так решение среза не гоняет
    /// сеть на каждый возврат.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _session.EnsureLoadedAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_session.IsAuthenticated)
        {
            ApplySignedOutState();
            return;
        }

        IsAuthenticated = true;

        if (HasLoaded)
        {
            return;
        }

        HasLoaded = true;

        await LoadFirstPageAsync(FirstPageMode.Initial, cancellationToken);
    }

    /// <summary>
    /// Сбрасывает экран после завершения сессии.
    /// </summary>
    /// <remarks>
    /// Поколение сдвигается: ответ, который ещё летит, не вернёт отзывы прошлого пользователя
    /// на экран.
    /// </remarks>
    public void ApplySignedOutState()
    {
        _generation++;
        _nextCursor = null;

        IsAuthenticated = false;
        IsFirstLoading = false;
        IsLoadingMore = false;
        IsRefreshing = false;
        HasLoaded = false;
        ErrorMessage = null;
        LoadMoreError = null;
        Announcement = null;

        Items.Clear();

        NotifyViewState();
    }

    /// <summary>
    /// Применяет результат правки: строка меняется на месте, список не перечитывается.
    /// </summary>
    /// <param name="item">Строка, которую правили.</param>
    /// <param name="response">Ответ сервера с новым статусом и временем правки.</param>
    /// <param name="rating">Отправленная оценка.</param>
    /// <param name="text">Отправленный текст; <c>null</c> — без комментария.</param>
    public void ApplyUpdated(
        MyReviewListItem item,
        UpdateReviewResponse response,
        int rating,
        string? text)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(response);

        item.ApplyUpdated(response, rating, text);
        Announcement = UpdatedAnnouncement;
    }

    /// <summary>
    /// Удаляет отзыв после подтверждения на странице.
    /// </summary>
    /// <remarks>
    /// Возвращает текст для нативного диалога либо <c>null</c>, когда всё получилось (или
    /// сессия истекла — тогда страница уже ушла на вкладку «Профиль»). Повторное нажатие
    /// во время запроса игнорируется: строка помечена <see cref="MyReviewListItem.IsDeleting"/>.
    /// </remarks>
    /// <param name="item">Удаляемая строка.</param>
    /// <returns>Причина ошибки для показа либо <c>null</c>.</returns>
    public async Task<string?> DeleteAsync(MyReviewListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.IsDeleting)
        {
            return null;
        }

        item.IsDeleting = true;

        try
        {
            await _reviewsApi.DeleteReviewAsync(item.Id);

            RemoveItem(item);
            Announcement = DeletedAnnouncement;

            await FillEmptiedPageAsync();

            return null;
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();

            return null;
        }
        catch (BarberSearchApiException exception)
        {
            if (exception.StatusCode == HttpStatusCode.NotFound)
            {
                // Отзыв уже удалён — например, с другого устройства: для списка это успех.
                RemoveItem(item);
                Announcement = AlreadyDeletedAnnouncement;

                await FillEmptiedPageAsync();

                return null;
            }

            return exception.StatusCode switch
            {
                HttpStatusCode.Forbidden => DeleteForbiddenText,
                HttpStatusCode.TooManyRequests => TooManyRequestsText,
                _ => DeleteFailedText,
            };
        }
        catch (HttpRequestException)
        {
            return NetworkErrorText;
        }
        catch (OperationCanceledException)
        {
            // Таймаут транспорта приходит отменой: для человека это отсутствие связи.
            return NetworkErrorText;
        }
        finally
        {
            item.IsDeleting = false;
        }
    }

    /// <summary>
    /// Обновляет первую страницу свайпом.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!IsAuthenticated)
        {
            IsRefreshing = false;

            return;
        }

        IsRefreshing = true;

        try
        {
            await LoadFirstPageAsync(FirstPageMode.Silent, CancellationToken.None);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Догружает следующую страницу.
    /// </summary>
    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!CanLoadMore || !IsAuthenticated || IsLoadingMore || IsFirstLoading)
        {
            return;
        }

        IsLoadingMore = true;
        LoadMoreError = null;

        var generation = _generation;

        try
        {
            var page = await _reviewsApi.GetMyReviewsAsync(_nextCursor, PageSize, CancellationToken.None);

            if (generation != _generation)
            {
                return;
            }

            AppendItems(page.Items);
            _nextCursor = page.NextCursor;
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();
        }
        catch (BarberSearchApiException exception)
        {
            if (generation == _generation)
            {
                LoadMoreError = exception.StatusCode == HttpStatusCode.TooManyRequests
                    ? TooManyRequestsText
                    : LoadMoreErrorText;
            }
        }
        catch (HttpRequestException)
        {
            if (generation == _generation)
            {
                LoadMoreError = NetworkErrorText;
            }
        }
        catch (OperationCanceledException)
        {
            if (generation == _generation)
            {
                LoadMoreError = NetworkErrorText;
            }
        }
        finally
        {
            IsLoadingMore = false;

            NotifyViewState();
        }
    }

    /// <summary>
    /// Повторяет первую загрузку после ошибки.
    /// </summary>
    [RelayCommand]
    private async Task RetryAsync()
    {
        if (!IsAuthenticated)
        {
            return;
        }

        ErrorMessage = null;
        NotifyViewState();

        await LoadFirstPageAsync(FirstPageMode.Initial, CancellationToken.None);
    }

    /// <summary>
    /// Повторяет догрузку после ошибки, не трогая загруженные строки.
    /// </summary>
    [RelayCommand]
    private async Task RetryLoadMoreAsync()
    {
        LoadMoreError = null;
        NotifyViewState();

        await LoadMoreAsync();
    }

    /// <summary>
    /// Закрывает баннер о результате действия.
    /// </summary>
    [RelayCommand]
    private void DismissAnnouncement() => Announcement = null;

    /// <summary>
    /// Просит страницу открыть правку отзыва.
    /// </summary>
    /// <param name="item">Строка списка.</param>
    [RelayCommand]
    private void Edit(MyReviewListItem? item)
    {
        if (item is null)
        {
            return;
        }

        EditRequested?.Invoke(this, item);
    }

    /// <summary>
    /// Читает первую страницу и заменяет список.
    /// </summary>
    /// <remarks>
    /// Поколение сдвигается на старте: ответ, пришедший после обновления или завершения
    /// сессии, отбрасывается. Ошибка тихого обновления не стирает уже загруженные строки.
    /// </remarks>
    private async Task LoadFirstPageAsync(FirstPageMode mode, CancellationToken cancellationToken)
    {
        _generation++;
        var generation = _generation;

        ErrorMessage = null;
        LoadMoreError = null;

        if (mode == FirstPageMode.Initial)
        {
            IsFirstLoading = true;
        }

        NotifyViewState();

        try
        {
            var page = await _reviewsApi.GetMyReviewsAsync(cursor: null, PageSize, cancellationToken);

            if (generation != _generation)
            {
                return;
            }

            Items.Clear();

            foreach (var review in page.Items)
            {
                Items.Add(new MyReviewListItem(review));
            }

            _nextCursor = page.NextCursor;
            IsFirstLoading = false;

            NotifyViewState();
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();
        }
        catch (BarberSearchApiException exception)
        {
            SetPageFailure(generation, exception.StatusCode == HttpStatusCode.TooManyRequests
                ? TooManyRequestsText
                : LoadErrorText);
        }
        catch (HttpRequestException)
        {
            SetPageFailure(generation, NetworkErrorText);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SetPageFailure(generation, NetworkErrorText);
        }
        catch (OperationCanceledException)
        {
            // Отмена страницей — не ошибка.
        }
        finally
        {
            if (generation == _generation)
            {
                IsFirstLoading = false;
                NotifyViewState();
            }
        }
    }

    /// <summary>
    /// Показывает ошибку страницы, но не стирает уже загруженные строки.
    /// </summary>
    private void SetPageFailure(int generation, string message)
    {
        if (generation != _generation)
        {
            return;
        }

        IsFirstLoading = false;

        if (Items.Count == 0)
        {
            ErrorMessage = message;
        }

        NotifyViewState();
    }

    /// <summary>
    /// Догружает следующую страницу, если удаление опустошило список.
    /// </summary>
    /// <remarks>
    /// Пустая страница при наличии курсора — это не конец списка: следующая страница
    /// существует, и показывать пустое состояние значило бы врать. Если курсора нет —
    /// отзывов действительно больше нет.
    /// </remarks>
    private async Task FillEmptiedPageAsync()
    {
        NotifyViewState();

        if (Items.Count == 0 && CanLoadMore)
        {
            await LoadMoreAsync();
        }
    }

    /// <summary>
    /// Убирает строку из списка.
    /// </summary>
    private void RemoveItem(MyReviewListItem item)
    {
        Items.Remove(item);
        NotifyViewState();
    }

    /// <summary>
    /// Дополняет список следующей страницей.
    /// </summary>
    private void AppendItems(IReadOnlyList<MyReviewResponse> reviews)
    {
        foreach (var review in reviews)
        {
            Items.Add(new MyReviewListItem(review));
        }

        NotifyViewState();
    }

    /// <summary>
    /// Гасит сессию: пара мертва, страница вернёт на вкладку «Профиль».
    /// </summary>
    private async Task EndSessionAsync()
    {
        ApplySignedOutState();

        await _session.ClearAsync(CancellationToken.None);
    }

    /// <summary>
    /// Поднимает уведомления обо всех вычисляемых свойствах экрана.
    /// </summary>
    private void NotifyViewState()
    {
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ShowList));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(CanLoadMore));
        OnPropertyChanged(nameof(ShowLoadMoreError));
    }

    /// <summary>
    /// Режим загрузки первой страницы: показать индикатор или обновить молча.
    /// </summary>
    private enum FirstPageMode
    {
        Initial,
        Silent,
    }
}
